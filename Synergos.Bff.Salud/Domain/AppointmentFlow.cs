using Synergos.Bff.Core;
using Synergos.Bff.Salud.Clients;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// El flujo de agendar una cita con copago — <b>el ORDEN, que es lo del dominio</b>.
/// </summary>
/// <remarks>
/// <para><b>Lo que quedó acá tras promover la máquina de sagas.</b> Deshacer, reintentar,
/// rendirse y avisar son iguales en los ocho dominios y viven en
/// <see cref="SagaEngine{TSaga}"/>. Lo que no se puede compartir es <i>en qué orden van los
/// pasos</i>: que el permiso va antes del cupo, que el cupo va antes del cobro, y que capturar va
/// antes de confirmar. Eso es el negocio, y por eso es lo único que sigue viviendo en este
/// fichero.</para>
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
    private readonly SagaEngine<AppointmentSaga> _sagas;
    private readonly TimeProvider _clock;
    private readonly ILogger<AppointmentFlow> _log;

    public AppointmentFlow(
        SaludCapabilities caps, SagaEngine<AppointmentSaga> sagas,
        TimeProvider clock, ILogger<AppointmentFlow> log)
    {
        _caps = caps;
        _sagas = sagas;
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

        saga = new AppointmentSaga(sagaId, patient, professional, window, SagaStatus.Running,
            null, null, null, Money.Zero(Money.Cop), Array.Empty<Compensation>(), null, _clock.GetUtcNow());

        // 2. Cupo. Es el paso barato y reversible: se toma antes de hablar de plata.
        var hold = await _caps.HoldAsync(resourceId, window, patient, saga.KeyFor("hold"), ct);
        if (!hold.IsOk) return Result.Rejected<AppointmentSaga>(hold.Rejection!);

        saga = saga with
        {
            HoldId = hold.Value.Id,
            Compensations = new[]
            {
                Compensation.For(SaludCompensations.ReleaseBookingHold, hold.Value.Id, "cita no confirmada"),
            },
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
            await _sagas.CompensateAsync(saga.Id, "el cobro no se pudo autorizar", ct);
            return Result.Rejected<AppointmentSaga>(pago.Rejection!);
        }

        saga = saga with
        {
            PaymentId = pago.Value.Id,
            Total = total,
            // La autorización se ANOTA como compensable en el mismo momento en que existe. Si
            // se anotara después de confirmar, una caída en medio dejaría plata reservada en la
            // tarjeta del paciente sin nada que la libere.
            Compensations = saga.Compensations
                .Append(Compensation.For(SaludCompensations.VoidPayment, pago.Value.Id, "cita no confirmada"))
                .ToList(),
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
        if (saga.Status == SagaStatus.Completed) return Result.Ok(saga);   // idempotente
        if (saga.Status != SagaStatus.Running)
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
                await _sagas.CompensateAsync(saga.Id, "el cobro no se pudo capturar", ct);
                return Result.Rejected<AppointmentSaga>(capturado.Rejection!);
            }

            // La compensación del pago pasa de "liberar autorización" a "devolver": ya se movió
            // plata, y liberar una autorización capturada Payments lo rechaza. Sin este cambio,
            // la compensación fallaría siempre y quedaría colgada para siempre.
            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == SaludCompensations.VoidPayment && c.IsPending
                        ? c with { Kind = SaludCompensations.RefundPayment }
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
            await _sagas.CompensateAsync(saga.Id, "la cita no se pudo confirmar tras cobrar", ct);
            return Result.Rejected<AppointmentSaga>(reserva.Rejection!);
        }

        // Salió: ya no hay nada que deshacer. Las compensaciones se marcan como no aplicables
        // —hechas— para que el barrido no las intente.
        var ahora = _clock.GetUtcNow();
        saga = saga with
        {
            Status = SagaStatus.Completed,
            ReservationId = reserva.Value.Id,
            LastError = null,
            Compensations = saga.Compensations.Select(c => c.IsPending ? c with { DoneAtUtc = ahora } : c).ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>Cancela una cita todavía sin confirmar: suelta el cupo y libera el cobro.</summary>
    public Task<Result<AppointmentSaga>> CancelAsync(string sagaId, CancellationToken ct)
        => _sagas.CompensateAsync(sagaId, "cancelada por el paciente", ct);

    /// <summary>Vuelve a intentar lo que se había rendido.</summary>
    public Task<Result<AppointmentSaga>> RetryStuckAsync(string sagaId, CancellationToken ct)
        => _sagas.RetryStuckAsync(sagaId, ct);

    public Result<AppointmentSaga> Get(string id)
        => _sagas.Find(id) is { } s
            ? Result.Ok(s)
            : Rejection.NotFound("salud.appointment_not_found", $"No existe la cita {id}.");

    public IReadOnlyList<AppointmentSaga> PendingCompensations() => _sagas.PendingCompensations();
}
