using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Storage;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// El flujo de agendar una cita con copago — <b>el orquestador propiamente dicho</b>.
/// </summary>
/// <remarks>
/// <para><b>Lo que aporta y ninguna capacidad puede aportar: el ORDEN y la COMPENSACIÓN.</b>
/// Booking sabe apartar, Payments sabe cobrar y Consent sabe si hay permiso; ninguno de los tres
/// sabe que el permiso va antes del cupo, que el cupo va antes del cobro, y que si la
/// confirmación falla hay que devolver la plata. Eso es el negocio, y por eso vive acá.</para>
///
/// <para><b>Por qué el flujo tiene DOS fases.</b> Agendar apart­a el cupo y <i>autoriza</i>
/// —reserva el cupo en el medio de pago, sin mover plata—; confirmar <i>captura</i> y confirma.
/// No es cortesía con la interfaz: es lo que hace que el caso más común de fallo —el paciente se
/// arrepiente, o la ventana se le vence— no cueste una devolución. Y es el mismo razonamiento
/// del hold de Booking, aplicado un nivel más arriba.</para>
///
/// <para><b>El orden dentro de confirmar importa, y es el único sitio donde puede doler:</b>
/// primero se captura y después se confirma el cupo. Al revés —confirmar y luego capturar— un
/// fallo del cobro dejaría una cita confirmada que nadie pagó, y recuperarse de eso exige
/// llamar al paciente. Con este orden, el fallo deja plata cobrada sin cita, que <b>sí</b> se
/// deshace sola: se devuelve.</para>
/// </remarks>
public sealed class AppointmentFlow
{
    /// <summary>El propósito de consentimiento que exige agendar.</summary>
    public const string ConsentPurpose = "salud.agenda";

    private readonly SaludCapabilities _caps;
    private readonly ISagaStore _sagas;
    private readonly Compensator _compensator;
    private readonly CompensationAlert _alert;
    private readonly TimeProvider _clock;
    private readonly ILogger<AppointmentFlow> _log;

    public AppointmentFlow(
        SaludCapabilities caps, ISagaStore sagas, Compensator compensator, CompensationAlert alert,
        TimeProvider clock, ILogger<AppointmentFlow> log)
    {
        _caps = caps;
        _sagas = sagas;
        _compensator = compensator;
        _alert = alert;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Fase 1: comprueba permiso, aparta el cupo, cotiza y autoriza el cobro.
    /// </summary>
    public async Task<Result<AppointmentSaga>> ScheduleAsync(
        Ref patient, Ref professional, string resourceId, Ref service, TimeWindow window, string sagaId, CancellationToken ct)
    {
        // La saga existe ANTES de tocar nada. Si el proceso se cae después del primer paso, lo
        // que se hizo queda escrito con su identificador — y como las llaves derivan de él,
        // repetir la llamada con el mismo sagaId no duplica nada.
        var saga = _sagas.Find(sagaId);
        if (saga is not null)
        {
            // Reintento de la misma petición: se devuelve lo que hay. La alternativa —rehacer—
            // tomaría un segundo cupo con una llave distinta.
            return Result.Ok(saga);
        }

        // 1. Consentimiento. Va primero porque es lo único que puede prohibir el flujo entero, y
        //    comprobarlo después de apartar un cupo obligaría a soltarlo.
        var consent = await _caps.CheckConsentAsync(patient, ConsentPurpose, ct);
        if (!consent.IsOk) return Result.Rejected<AppointmentSaga>(consent.Rejection!);

        saga = new AppointmentSaga(sagaId, patient, professional, window, SagaStatus.AwaitingConfirmation,
            null, null, null, Money.Zero(Money.Cop), Array.Empty<Compensation>(), null, _clock.GetUtcNow());

        // 2. Cupo. Es el paso barato y reversible: se toma antes de hablar de plata.
        var hold = await _caps.HoldAsync(resourceId, window, patient, saga.KeyFor("hold"), ct);
        if (!hold.IsOk) return Result.Rejected<AppointmentSaga>(hold.Rejection!);

        saga = saga with
        {
            HoldId = hold.Value.Id,
            Compensations = new[] { Comp(CompensationKind.ReleaseBookingHold, hold.Value.Id, "cita no confirmada") },
        };
        _sagas.Put(saga);

        // 3. Cuánto cuesta. Si no hay precio publicado, no hay copago: la cita sigue.
        var quote = await _caps.QuoteAsync(service, ct);
        var total = quote.IsOk
            ? Money.Of(quote.Value.Total.Amount, quote.Value.Total.Currency)
            : Money.Zero(Money.Cop);

        if (total.IsZero)
        {
            // Sin copago no hay cobro que autorizar — y un pago de cero lo rechazaría Payments.
            saga = saga with { Total = total };
            _sagas.Put(saga);
            return Result.Ok(saga);
        }

        // 4. Autorizar: reserva cupo en el medio de pago SIN mover plata.
        var pago = await _caps.AuthorizeAsync(Ref.Create("salud.cita", sagaId), patient, total, saga.KeyFor("authorize"), ct);
        if (!pago.IsOk)
        {
            // El cobro no salió: se suelta el cupo y se cierra. Acá compensar es barato porque
            // todavía no se movió plata — que es exactamente para lo que existen las dos fases.
            saga = saga with { LastError = pago.Rejection!.ToString(), Total = total };
            _sagas.Put(saga);
            await CompensateAsync(saga.Id, "el cobro no se pudo autorizar", ct);
            return Result.Rejected<AppointmentSaga>(pago.Rejection!);
        }

        saga = saga with
        {
            PaymentId = pago.Value.Id,
            Total = total,
            // La autorización se ANOTA como compensable en el mismo momento en que existe. Si
            // se anotara después de confirmar, una caída en medio dejaría plata reservada en la
            // tarjeta del paciente sin nada que la libere.
            Compensations = saga.Compensations.Append(Comp(CompensationKind.VoidPayment, pago.Value.Id, "cita no confirmada")).ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>
    /// Fase 2: captura el cobro y confirma el cupo. Si algo falla, deshace.
    /// </summary>
    public async Task<Result<AppointmentSaga>> ConfirmAsync(string sagaId, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null)
        {
            return Rejection.NotFound("salud.appointment_not_found", $"No existe la cita {sagaId}.");
        }
        if (saga.Status == SagaStatus.Confirmed) return Result.Ok(saga);   // idempotente
        if (saga.Status != SagaStatus.AwaitingConfirmation)
        {
            return Rejection.Conflict("salud.not_confirmable", $"La cita está {saga.Status}.");
        }

        // 1. Capturar. A partir de acá hay plata movida, y todo fallo cuesta una devolución.
        if (saga.PaymentId is { } paymentId)
        {
            var capturado = await _caps.CaptureAsync(paymentId, saga.KeyFor("capture"), ct);
            if (!capturado.IsOk)
            {
                saga = saga with { LastError = capturado.Rejection!.ToString() };
                _sagas.Put(saga);
                await CompensateAsync(saga.Id, "el cobro no se pudo capturar", ct);
                return Result.Rejected<AppointmentSaga>(capturado.Rejection!);
            }

            // La compensación del pago pasa de "liberar autorización" a "devolver": ya se movió
            // plata, y liberar una autorización capturada Payments lo rechaza. Sin este cambio,
            // la compensación fallaría siempre y quedaría colgada para siempre.
            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == CompensationKind.VoidPayment && c.IsPending
                        ? c with { Kind = CompensationKind.RefundPayment }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 2. Confirmar el cupo. Es el paso que puede dejar plata cobrada sin cita.
        var reserva = await _caps.ConfirmHoldAsync(saga.HoldId!, saga.KeyFor("confirm"), ct);
        if (!reserva.IsOk)
        {
            _log.LogWarning("La cita {Saga} no se pudo confirmar tras capturar ({Error}); se compensa.",
                saga.Id, reserva.Rejection);
            saga = saga with { LastError = reserva.Rejection!.ToString() };
            _sagas.Put(saga);
            await CompensateAsync(saga.Id, "la cita no se pudo confirmar tras cobrar", ct);
            return Result.Rejected<AppointmentSaga>(reserva.Rejection!);
        }

        // Salió: ya no hay nada que deshacer. Las compensaciones se marcan como no aplicables
        // —hechas— para que el barrido no las intente.
        var ahora = _clock.GetUtcNow();
        saga = saga with
        {
            Status = SagaStatus.Confirmed,
            ReservationId = reserva.Value.Id,
            LastError = null,
            Compensations = saga.Compensations.Select(c => c.IsPending ? c with { DoneAtUtc = ahora } : c).ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>Cancela una cita todavía sin confirmar: suelta el cupo y libera el cobro.</summary>
    public Task<Result<AppointmentSaga>> CancelAsync(string sagaId, CancellationToken ct)
        => CompensateAsync(sagaId, "cancelada por el paciente", ct);

    /// <summary>
    /// Ejecuta las compensaciones pendientes de una saga.
    /// </summary>
    /// <remarks>
    /// <b>Se llama desde dos sitios y hace lo mismo en los dos:</b> en línea cuando un paso
    /// falla, y desde el barrido de fondo cuando llega la hora de reintentar. Que sea el mismo
    /// camino es lo que evita que la segunda vez se haga distinto de la primera.
    /// </remarks>
    public async Task<Result<AppointmentSaga>> CompensateAsync(string sagaId, string reason, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null)
        {
            return Rejection.NotFound("salud.appointment_not_found", $"No existe la cita {sagaId}.");
        }
        if (saga.Status == SagaStatus.Confirmed)
        {
            return Rejection.Conflict("salud.already_confirmed",
                "La cita ya se confirmó. Deshacerla es una cancelación con su política, no una compensación.");
        }

        var ahora = _clock.GetUtcNow();
        var actualizadas = new List<Compensation>(saga.Compensations.Count);

        foreach (var c in saga.Compensations)
        {
            // Solo lo pendiente, lo que no se rindió, Y cuyo turno llegó. Las tres condiciones
            // cuentan: respetar el retroceso evita que un barrido cada minuto martillee una
            // capacidad caída, y saltarse lo rendido es lo que hace que rendirse SIGNIFIQUE algo
            // — sin eso, una compensación agotada se reintentaría cada minuto para siempre,
            // gritando el mismo error en el log y tapando los que sí se pueden atender.
            if (!c.IsPending || c.IsStuck || (c.NextAttemptUtc is { } cuando && ahora < cuando))
            {
                actualizadas.Add(c);
                continue;
            }
            actualizadas.Add(await _compensator.TryAsync(saga, c with { Reason = reason }, ct));
        }

        var quedan = actualizadas.Any(c => c.IsPending);
        var colgadas = actualizadas.Any(c => c.IsStuck);

        saga = saga with
        {
            Compensations = actualizadas,
            Status = colgadas ? SagaStatus.CompensationFailed
                : quedan ? SagaStatus.Compensating
                : SagaStatus.Compensated,
        };

        // El aviso sale UNA vez por vez que la saga cae en colgada, no una por vuelta del
        // barrido. Sin esa guarda, la guardia recibiría el mismo correo cada minuto hasta que
        // Api.Notifications lo cortara por tope de frecuencia — y ese corte se llevaría por
        // delante los avisos de las demás citas.
        if (saga.Status == SagaStatus.CompensationFailed && saga.AlertedAtUtc is null)
        {
            saga = await AvisarAsync(saga, ahora, ct);
        }

        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>
    /// Vuelve a intentar lo que se había rendido. <b>Es la puerta de la persona.</b>
    /// </summary>
    /// <remarks>
    /// Rendirse a los ocho intentos es correcto mientras haya una forma de decir «ya arreglé la
    /// causa, probá otra vez». Sin ella, «se rinde» sería «se abandona», y la única salida a una
    /// devolución colgada sería tocarla a mano en la capacidad — por fuera del rastro de la saga.
    /// </remarks>
    public async Task<Result<AppointmentSaga>> RetryStuckAsync(string sagaId, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null)
        {
            return Rejection.NotFound("salud.appointment_not_found", $"No existe la cita {sagaId}.");
        }
        if (saga.Stuck.Count == 0)
        {
            return Rejection.Conflict("salud.nothing_stuck",
                "Esta cita no tiene compensaciones rendidas. Las que están en curso las reintenta el barrido sola.");
        }

        _log.LogInformation("Reintento manual de las compensaciones rendidas de la cita {Saga}.", saga.Id);

        saga = saga with
        {
            Compensations = saga.Compensations
                .Select(c => c.IsStuck ? c with { Attempts = 0, NextAttemptUtc = null } : c)
                .ToList(),
            // Se rearma el aviso: si vuelve a colgarse tras el arreglo, la guardia tiene que
            // enterarse otra vez. La llave lleva AlertsSent, así que el segundo aviso no lo
            // confunde Notifications con el primero.
            AlertedAtUtc = null,
        };
        _sagas.Put(saga);

        return await CompensateAsync(sagaId, "reintento pedido por una persona", ct);
    }

    /// <summary>Manda el aviso y deja anotado en la saga qué pasó con él.</summary>
    private async Task<AppointmentSaga> AvisarAsync(AppointmentSaga saga, DateTimeOffset ahora, CancellationToken ct)
    {
        var motivo = await _alert.RaiseAsync(saga, ct);

        if (motivo is null)
        {
            _log.LogError("COMPENSACIÓN COLGADA en la cita {Saga}: avisado a la guardia.", saga.Id);
            return saga with { AlertedAtUtc = ahora, AlertsSent = saga.AlertsSent + 1 };
        }

        if (motivo.IsTransient)
        {
            // Notifications caída es lo mismo que cualquier otra capacidad caída: se reintenta al
            // siguiente barrido. NO se marca como avisada, que es justo lo que permite el
            // reintento.
            _log.LogWarning("No se pudo avisar de la cita {Saga} ({Error}); se reintenta en el barrido.",
                saga.Id, motivo);
            return saga;
        }

        // Falta la plantilla, o no hay a quién avisar. Eso no lo arregla reintentar: lo arregla
        // una persona tocando la configuración o autorando la plantilla, y repetirlo cada minuto
        // solo llenaría el log del mismo error tapando el que importa. Se grita una vez.
        _log.LogError(
            "COMPENSACIÓN COLGADA en la cita {Saga} y NO SE PUDO AVISAR A NADIE ({Error}). Revisa Salud:Alerts y la plantilla '{Plantilla}'.",
            saga.Id, motivo, _alert.TemplateKey);
        return saga with { AlertedAtUtc = ahora };
    }

    public Result<AppointmentSaga> Get(string id)
        => _sagas.Find(id) is { } s
            ? Result.Ok(s)
            : Rejection.NotFound("salud.appointment_not_found", $"No existe la cita {id}.");

    public IReadOnlyList<AppointmentSaga> PendingCompensations() => _sagas.WithPendingCompensations();

    private static Compensation Comp(CompensationKind kind, string targetId, string reason)
        => new(Guid.NewGuid().ToString("n"), kind, targetId, reason);
}
