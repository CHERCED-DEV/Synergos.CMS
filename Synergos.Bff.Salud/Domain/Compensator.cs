using Synergos.Bff.Salud.Clients;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// Ejecuta una compensación pendiente.
/// </summary>
/// <remarks>
/// <para><b>Separado del flujo a propósito.</b> Lo mismo tiene que poder correr en dos momentos
/// muy distintos: en línea, cuando el paso falla y todavía hay un usuario esperando; y horas
/// después, desde un barrido de fondo, cuando la capacidad que estaba caída volvió. Un solo
/// camino para las dos evita que la segunda vez se haga distinto de la primera.</para>
/// </remarks>
public sealed class Compensator
{
    /// <summary>Cuántos intentos antes de rendirse y pedir una persona.</summary>
    public const int MaxAttempts = 8;

    private readonly SaludCapabilities _caps;
    private readonly TimeProvider _clock;
    private readonly ILogger<Compensator> _log;

    public Compensator(SaludCapabilities caps, TimeProvider clock, ILogger<Compensator> log)
    {
        _caps = caps;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Espera antes del intento <paramref name="attempts"/>: 1, 2, 4… minutos, con techo.
    /// </summary>
    /// <remarks>
    /// Retroceso exponencial y no reintento inmediato: la causa habitual de que una compensación
    /// falle es que la capacidad está caída, y martillearla no la levanta — solo alarga la caída.
    /// </remarks>
    public static TimeSpan Backoff(int attempts)
        => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(attempts, 6))));

    /// <summary>Intenta una compensación. Devuelve cómo queda.</summary>
    public async Task<Compensation> TryAsync(AppointmentSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow();
        var intentos = pendiente.Attempts + 1;

        var resultado = pendiente.Kind switch
        {
            CompensationKind.ReleaseBookingHold => await Envolver(_caps.ReleaseHoldAsync(pendiente.TargetId, ct)),
            CompensationKind.VoidPayment => await Envolver(_caps.VoidAsync(pendiente.TargetId, ct)),
            CompensationKind.RefundPayment => await RefundAsync(saga, pendiente, ct),
            _ => Rejection.Invalid("salud.unknown_compensation", $"No sé deshacer {pendiente.Kind}."),
        };

        if (resultado is null)
        {
            _log.LogInformation("Compensación {Kind} de la saga {Saga} completada en el intento {N}.",
                pendiente.Kind, saga.Id, intentos);
            return pendiente with { Attempts = intentos, DoneAtUtc = ahora, LastError = null, NextAttemptUtc = null };
        }

        // Se rinde a gritos y NO se marca como hecha: una compensación que se da por buena sin
        // haberse ejecutado es plata que se quedó cobrada sin servicio, y nadie se entera.
        if (intentos >= MaxAttempts)
        {
            _log.LogError(
                "COMPENSACIÓN COLGADA: {Kind} sobre {Target} de la saga {Saga} falló {N} veces ({Error}). Necesita una persona.",
                pendiente.Kind, pendiente.TargetId, saga.Id, intentos, resultado);
            return pendiente with { Attempts = intentos, LastError = resultado.ToString(), NextAttemptUtc = null };
        }

        _log.LogWarning("Compensación {Kind} de la saga {Saga} falló ({Error}); reintento {N}.",
            pendiente.Kind, saga.Id, resultado, intentos);
        return pendiente with { Attempts = intentos, LastError = resultado.ToString(), NextAttemptUtc = ahora + Backoff(intentos) };
    }

    /// <summary>
    /// Devuelve lo capturado. Si ya está devuelto, se da por hecha.
    /// </summary>
    /// <remarks>
    /// El caso que importa: un reintento tras un timeout donde la devolución <i>sí</i> había
    /// salido. La llave determinista hace que Payments devuelva la misma operación en vez de
    /// devolver dos veces — pero además se comprueba el saldo devolvible, porque si el reintento
    /// llega con otra llave (otro proceso, otra vida del barrido) la llave ya no protege.
    /// </remarks>
    private async Task<Rejection?> RefundAsync(AppointmentSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var pago = await _caps.GetPaymentAsync(pendiente.TargetId, ct);
        if (!pago.IsOk) return pago.Rejection;

        var devolvible = Money.Of(pago.Value.Refundable.Amount, pago.Value.Refundable.Currency);
        if (devolvible.IsZero)
        {
            // Ya no queda nada por devolver: o se devolvió en un intento anterior que no llegó a
            // anotarse, o alguien lo devolvió a mano. En los dos casos la compensación cumplió.
            return null;
        }

        var r = await _caps.RefundAsync(pendiente.TargetId, devolvible, pendiente.Reason, saga.KeyFor($"refund:{pendiente.Id}"), ct);
        return r.IsOk ? null : r.Rejection;
    }

    private static async Task<Rejection?> Envolver<T>(Task<Result<T>> llamada)
    {
        var r = await llamada;
        return r.IsOk ? null : r.Rejection;
    }
}
