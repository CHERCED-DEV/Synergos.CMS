using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// El flujo de un alquiler: apartar la ventana, retener la garantía, cobrar, confirmar — y
/// después, días más tarde, devolver.
/// </summary>
/// <remarks>
/// <para><b>El orden lo decide <c>close_doors_last</c></b>: primero lo reversible —apartar y
/// autorizar— y al final lo que cierra la puerta —confirmar los apartados—, cuando ya no queda
/// nada detrás que pueda fallar.</para>
///
/// <para><b>Y la saga se cierra al reservar.</b> Dejarla <c>Running</c> hasta la devolución la
/// pondría a merced del barrido de abandono, que deshaceria un alquiler vivo a las pocas horas.
/// La devolución opera sobre una saga ya liquidada, igual que la cancelación de un viaje ya
/// confirmado: <c>Bff.Core</c> rechaza compensar eso con todas las letras, porque deshacerlo es
/// una cancelación con su política y no una compensación.</para>
/// </remarks>
public sealed class RentalFlow
{
    /// <summary>Cuántas unidades admite un alquiler de una vez.</summary>
    public const int MaxUnits = 20;

    private readonly AlquilerCapabilities _caps;
    private readonly SagaEngine<RentalSaga> _sagas;
    private readonly TimeProvider _clock;
    private readonly ILogger<RentalFlow> _log;

    /// <summary>Construye el flujo.</summary>
    /// <param name="caps">Las dos capacidades.</param>
    /// <param name="sagas">La máquina de sagas.</param>
    /// <param name="clock">El reloj.</param>
    /// <param name="log">Dónde se cuenta qué pasó.</param>
    public RentalFlow(
        AlquilerCapabilities caps, SagaEngine<RentalSaga> sagas, TimeProvider clock, ILogger<RentalFlow> log)
    {
        _caps = caps;
        _sagas = sagas;
        _clock = clock;
        _log = log;
    }

    /// <summary>Un alquiler por su identificador.</summary>
    /// <param name="id">Cuál.</param>
    /// <returns>El alquiler, o el rechazo.</returns>
    public Result<RentalSaga> Get(string id) => _sagas.Get(id);

    /// <summary>Aparta, retiene la garantía, cobra y confirma.</summary>
    /// <param name="renter">Quién alquila, como seudónimo.</param>
    /// <param name="equipmentRef">Qué equipo.</param>
    /// <param name="quantity">Cuántas unidades.</param>
    /// <param name="window">La ventana.</param>
    /// <param name="rentalTotal">Lo que se cobra, ya cotizado por quien vende.</param>
    /// <param name="deposit">Lo que se retiene.</param>
    /// <param name="sagaId">La llave de idempotencia, que es el id del alquiler.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El alquiler reservado, o el rechazo.</returns>
    public async Task<Result<RentalSaga>> ReserveAsync(
        Ref renter, string equipmentRef, int quantity, TimeWindow window,
        Money rentalTotal, Money deposit, string sagaId, CancellationToken ct)
    {
        if (Revisar(equipmentRef, quantity, rentalTotal, deposit) is { } malo)
        {
            return Result.Rejected<RentalSaga>(malo);
        }

        // La llave ANTES que el estado (§0.B.16): al revés, un reintento choca con lo que él
        // mismo creó.
        //
        // Y se resuelve con `Abrir` y no con un `Find` a secas, que es lo que tienta: una llave
        // protege contra DUPLICAR algo que existe, y de un alquiler deshecho entero no queda nada
        // que duplicar —la ventana volvió, la garantía se anuló—. Devolver la saga muerta no es
        // idempotencia: es dejar ENCERRADO a quien intentó alquilar y no pudo, para siempre,
        // porque la llave se deriva de qué alquila y por tanto nunca cambia (defecto #41).
        var slot = _sagas.Abrir(sagaId);
        if (slot.Reusar is { } previa)
        {
            return Result.Ok(previa);
        }

        // A partir de acá manda el identificador que decidió `Abrir`: un intento nuevo lleva el
        // suyo, y los sujetos del cobro y de la garantía salen de él. Seguir usando la llave cruda
        // haría que el segundo intento autorizara contra el MISMO sujeto que el muerto.
        var rentalId = slot.Id;

        var saga = new RentalSaga(
            rentalId, renter, SagaStatus.Running, equipmentRef, quantity, window,
            Array.Empty<UnitHold>(), null, null, rentalTotal, deposit,
            Money.Zero(rentalTotal.Currency), Settled: false, SettledAs: null,
            Array.Empty<Compensation>(), null, _clock.GetUtcNow());
        _sagas.Put(saga);

        var recurso = await _caps.FindResourceAsync(RentalSubject.For(equipmentRef), ct);
        if (!recurso.IsOk)
        {
            return await AbortarAsync(saga, recurso.Rejection!, "el equipo no tiene recurso reservable", ct);
        }

        // N unidades son N apartados: un hold de Api.Booking es UNA unidad de capacidad.
        for (var i = 0; i < quantity; i++)
        {
            var apartado = await _caps.HoldAsync(
                recurso.Value.Id, window, renter, saga.KeyFor($"hold:{i}"), ct);
            if (!apartado.IsOk)
            {
                return await AbortarAsync(saga, apartado.Rejection!,
                    "no quedan unidades del equipo en esas fechas", ct);
            }

            saga = saga with
            {
                Holds = saga.Holds.Append(new UnitHold(apartado.Value.Id, recurso.Value.Id)).ToList(),
                Compensations = saga.Compensations
                    .Append(Compensation.For(
                        AlquilerCompensations.ReleaseBookingHold, apartado.Value.Id, "alquiler no confirmado"))
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // La GARANTÍA: se autoriza y NO se captura. Va antes del cobro porque es lo que más
        // fácil se deshace —anular no mueve plata— y porque si no hay con qué responder por el
        // equipo, cobrar el alquiler sería cobrar por algo que no va a salir.
        if (!deposit.IsZero)
        {
            var garantia = await _caps.AuthorizeAsync(
                RentalSubject.DepositOf(rentalId), renter, deposit, saga.KeyFor("deposit"), ct);
            if (!garantia.IsOk)
            {
                return await AbortarAsync(saga, garantia.Rejection!, "no se pudo retener la garantía", ct);
            }

            saga = saga with
            {
                DepositPaymentId = garantia.Value.Id,
                Compensations = saga.Compensations
                    .Append(Compensation.For(
                        AlquilerCompensations.VoidPayment, garantia.Value.Id, "alquiler no confirmado"))
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // El COBRO del alquiler: éste sí se captura, porque es lo que se gana.
        var cobro = await _caps.AuthorizeAsync(
            RentalSubject.RentalOf(rentalId), renter, rentalTotal, saga.KeyFor("rental"), ct);
        if (!cobro.IsOk)
        {
            return await AbortarAsync(saga, cobro.Rejection!, "no se pudo autorizar el alquiler", ct);
        }

        saga = saga with
        {
            RentalPaymentId = cobro.Value.Id,
            Compensations = saga.Compensations
                .Append(Compensation.For(
                    AlquilerCompensations.VoidPayment, cobro.Value.Id, "alquiler no confirmado"))
                .ToList(),
        };
        _sagas.Put(saga);

        var capturado = await _caps.CaptureAsync(cobro.Value.Id, saga.KeyFor("capture"), ct);
        if (!capturado.IsOk)
        {
            return await AbortarAsync(saga, capturado.Rejection!, "no se pudo cobrar el alquiler", ct);
        }

        // Capturado: la compensación de ese cobro CAMBIA DE CARÁCTER — anular ya no sirve, hay
        // que devolver (`compensation_changes_character`). Sin reescribirla, la compensación
        // fallaría para siempre por una razón que no tiene nada que ver con el mundo real.
        saga = saga with
        {
            Compensations = saga.Compensations
                .Select(c => c.Kind == AlquilerCompensations.VoidPayment && c.TargetId == cobro.Value.Id
                    ? c with { Kind = AlquilerCompensations.RefundPayment, Reason = "alquiler no confirmado" }
                    : c)
                .ToList(),
        };
        _sagas.Put(saga);

        // Y AL FINAL lo que cierra la puerta: confirmar los apartados.
        var confirmados = new List<UnitHold>(saga.Holds.Count);
        foreach (var (apartado, i) in saga.Holds.Select((h, i) => (h, i)))
        {
            var reserva = await _caps.ConfirmHoldAsync(apartado.HoldId, saga.KeyFor($"confirm:{i}"), ct);
            if (!reserva.IsOk)
            {
                return await AbortarAsync(saga, reserva.Rejection!, "no se pudo confirmar el apartado", ct);
            }

            confirmados.Add(apartado with { ReservationId = reserva.Value.Id });

            // La del apartado también cambia de carácter: soltar pasa a cancelar.
            saga = saga with
            {
                Holds = confirmados.Concat(saga.Holds.Skip(confirmados.Count)).ToList(),
                Compensations = saga.Compensations
                    .Select(c => c.Kind == AlquilerCompensations.ReleaseBookingHold
                        && c.TargetId == apartado.HoldId
                        ? c with { Kind = AlquilerCompensations.CancelReservation, TargetId = reserva.Value.Id }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // La saga se CIERRA acá. Lo que queda por pasar —la devolución— es otra operación.
        saga = saga with { Status = SagaStatus.Completed, Compensations = Array.Empty<Compensation>() };
        _sagas.Put(saga);
        _log.LogInformation(
            "Alquiler {Saga}: {Unidades} unidad(es) de {Equipo} reservadas; garantía {Garantia} retenida.",
            rentalId, quantity, equipmentRef, deposit.Amount);

        return Result.Ok(saga);
    }

    /// <summary>
    /// El equipo volvió: cobra el daño de la garantía y suelta el resto.
    /// </summary>
    /// <remarks>
    /// <b>El caso normal es ANULAR</b>, no capturar: sin daño, la garantía nunca fue un cobro.
    /// Con daño se captura y se devuelve la diferencia, porque <c>capture</c> no admite monto
    /// parcial — medido contra el contrato de <c>Api.Payments</c>.
    /// </remarks>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="damage">Cuánto cobrar de la garantía.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El alquiler cerrado, o el rechazo.</returns>
    public async Task<Result<RentalSaga>> ReturnAsync(string rentalId, Money damage, CancellationToken ct)
        => await CerrarAsync(rentalId, damage, devolucion: true, ct);

    /// <summary>Cancela un alquiler antes de que el equipo salga.</summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="penalty">Cuánto retener por cancelar.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El alquiler cancelado, o el rechazo.</returns>
    public async Task<Result<RentalSaga>> CancelAsync(string rentalId, Money penalty, CancellationToken ct)
        => await CerrarAsync(rentalId, penalty, devolucion: false, ct);

    private async Task<Result<RentalSaga>> CerrarAsync(
        string rentalId, Money monto, bool devolucion, CancellationToken ct)
    {
        var buscado = _sagas.Get(rentalId);
        if (!buscado.IsOk)
        {
            return buscado;
        }

        var saga = buscado.Value;
        if (saga.Settled)
        {
            // Idempotente: cerrar dos veces devuelve lo de antes. Sin esto, un reintento
            // capturaría la garantía otra vez.
            return Result.Ok(saga);
        }

        if (saga.Status != SagaStatus.Completed)
        {
            return Result.Rejected<RentalSaga>(Rejection.Conflict(
                "alquiler.not_settleable",
                $"El alquiler {rentalId} está en {saga.Status} y todavía no se puede cerrar."));
        }

        if (monto.Amount < 0m)
        {
            return Result.Rejected<RentalSaga>(Rejection.Invalid(
                "alquiler.bad_amount", "El monto contra la garantía no puede ser negativo."));
        }

        if (monto.Amount > saga.Deposit.Amount)
        {
            return Result.Rejected<RentalSaga>(Rejection.Invalid(
                "alquiler.damage_exceeds_deposit",
                $"El monto ({monto.Amount}) supera la garantía retenida ({saga.Deposit.Amount})."));
        }

        if (saga.DepositPaymentId is { } garantia)
        {
            if (monto.IsZero)
            {
                var anulada = await _caps.VoidAsync(garantia, ct);
                if (!anulada.IsOk)
                {
                    return Result.Rejected<RentalSaga>(anulada.Rejection!);
                }
            }
            else
            {
                var capturada = await _caps.CaptureAsync(garantia, saga.KeyFor("settle:capture"), ct);
                if (!capturada.IsOk)
                {
                    return Result.Rejected<RentalSaga>(capturada.Rejection!);
                }

                var sobrante = Money.Of(saga.Deposit.Amount - monto.Amount, saga.Deposit.Currency);
                if (!sobrante.IsZero)
                {
                    var devuelta = await _caps.RefundAsync(
                        garantia, sobrante, "garantía no consumida", saga.KeyFor("settle:refund"), ct);
                    if (!devuelta.IsOk)
                    {
                        // NO se anota compensación: el barrido acabaría deshaciendo un alquiler
                        // que SÍ se cumplió. El rechazo sale hacia quien recibió el equipo, que
                        // puede repetir con la misma llave (#40).
                        return Result.Rejected<RentalSaga>(devuelta.Rejection!);
                    }
                }
            }
        }

        if (!devolucion)
        {
            foreach (var h in saga.Holds.Where(h => h.ReservationId is not null))
            {
                var cancelada = await _caps.CancelReservationAsync(h.ReservationId!, ct);
                if (!cancelada.IsOk)
                {
                    return Result.Rejected<RentalSaga>(cancelada.Rejection!);
                }
            }
        }

        var cerrada = saga with
        {
            Settled = true,
            SettledAs = devolucion ? "returned" : "cancelled",
            DamageCharged = monto,
        };
        _sagas.Put(cerrada);
        return Result.Ok(cerrada);
    }

    private async Task<Result<RentalSaga>> AbortarAsync(
        RentalSaga saga, Rejection motivo, string porQue, CancellationToken ct)
    {
        _sagas.Put(saga with { LastError = motivo.ToString() });
        await _sagas.CompensateAsync(saga.Id, porQue, ct);
        return Result.Rejected<RentalSaga>(motivo);
    }

    /// <summary>Lo que se rechaza antes de tocar ninguna capacidad.</summary>
    private static Rejection? Revisar(string equipmentRef, int quantity, Money rentalTotal, Money deposit)
    {
        if (string.IsNullOrWhiteSpace(equipmentRef))
        {
            return Rejection.Invalid("alquiler.equipment_required", "Un alquiler necesita su equipo.");
        }

        if (quantity < 1 || quantity > MaxUnits)
        {
            return Rejection.Invalid("alquiler.bad_quantity",
                $"La cantidad tiene que estar entre 1 y {MaxUnits}.");
        }

        if (rentalTotal.Amount <= 0m)
        {
            // Cero NO se acepta: es un precio válido que no falla en ninguna parte hasta que
            // alguien mira la factura (`a_failed_tryparse_is_not_a_value`).
            return Rejection.Invalid("alquiler.bad_total", "El total del alquiler tiene que ser mayor que cero.");
        }

        if (deposit.Amount < 0m)
        {
            return Rejection.Invalid("alquiler.bad_deposit", "La garantía no puede ser negativa.");
        }

        if (!string.Equals(rentalTotal.Currency, deposit.Currency, StringComparison.Ordinal))
        {
            return Rejection.Invalid("alquiler.currency_mismatch",
                "El alquiler y la garantía tienen que ir en la misma moneda.");
        }

        return null;
    }
}
