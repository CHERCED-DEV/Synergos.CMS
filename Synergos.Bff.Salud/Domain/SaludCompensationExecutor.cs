using Synergos.Bff.Core;
using Synergos.Bff.Salud.Clients;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// Cómo Salud deshace lo que Salud hizo.
/// </summary>
/// <remarks>
/// <b>Es todo lo que este dominio le tiene que enseñar al motor de sagas.</b> El retroceso, los
/// intentos, cuándo rendirse y a quién avisar los pone <c>Bff.Core</c>; lo que no puede saber es
/// que soltar un cupo es un <c>POST</c> a <c>booking/holds/{id}/release</c>. Un ejecutor de
/// treinta líneas por dominio es exactamente el tamaño que debería tener.
/// </remarks>
public sealed class SaludCompensationExecutor : ICompensationExecutor<AppointmentSaga>
{
    private readonly SaludCapabilities _caps;

    public SaludCompensationExecutor(SaludCapabilities caps) => _caps = caps;

    public async Task<Rejection?> UndoAsync(AppointmentSaga saga, Compensation pendiente, CancellationToken ct)
        => pendiente.Kind switch
        {
            SaludCompensations.ReleaseBookingHold => await Envolver(_caps.ReleaseHoldAsync(pendiente.TargetId, ct)),
            SaludCompensations.VoidPayment => await Envolver(_caps.VoidAsync(pendiente.TargetId, ct)),
            SaludCompensations.RefundPayment => await RefundAsync(saga, pendiente, ct),
            _ => Rejection.Invalid("salud.unknown_compensation", $"No sé deshacer {pendiente.Kind}."),
        };

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

        var r = await _caps.RefundAsync(pendiente.TargetId, devolvible, pendiente.Reason,
            saga.KeyFor($"refund:{pendiente.Id}"), ct);
        return r.IsOk ? null : r.Rejection;
    }

    private static async Task<Rejection?> Envolver<T>(Task<Result<T>> llamada)
    {
        var r = await llamada;
        return r.IsOk ? null : r.Rejection;
    }
}
