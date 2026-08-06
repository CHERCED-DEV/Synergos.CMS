using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Clients;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>
/// Cómo Viajes deshace lo que Viajes hizo.
/// </summary>
/// <remarks>
/// <b>Veinticinco líneas, y es TODO lo que este dominio le tiene que enseñar al motor.</b> El
/// retroceso exponencial, los ocho intentos, la guarda de «armada no es pendiente», el aviso
/// una-sola-vez y las llaves deterministas vinieron gratis de <c>Bff.Core</c>. Que el CUARTO
/// orquestador siga costando esto —y sin tocar ni una línea del motor— es la medida de si la
/// promoción valía la pena.
/// </remarks>
public sealed class ViajesCompensationExecutor : ICompensationExecutor<TripSaga>
{
    private readonly ViajesCapabilities _caps;

    public ViajesCompensationExecutor(ViajesCapabilities caps) => _caps = caps;

    public async Task<Rejection?> UndoAsync(TripSaga saga, Compensation pendiente, CancellationToken ct)
        => pendiente.Kind switch
        {
            ViajesCompensations.ReleaseBookingHold => await Envolver(_caps.ReleaseHoldAsync(pendiente.TargetId, ct)),
            ViajesCompensations.CancelReservation => await Envolver(_caps.CancelReservationAsync(pendiente.TargetId, ct)),
            ViajesCompensations.VoidPayment => await Envolver(_caps.VoidAsync(pendiente.TargetId, ct)),
            ViajesCompensations.RefundPayment => await RefundAsync(saga, pendiente, ct),
            _ => Rejection.Invalid("viajes.unknown_compensation", $"No sé deshacer {pendiente.Kind}."),
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
    private async Task<Rejection?> RefundAsync(TripSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var pago = await _caps.GetPaymentAsync(pendiente.TargetId, ct);
        if (!pago.IsOk) return pago.Rejection;

        var devolvible = Money.Of(pago.Value.Refundable.Amount, pago.Value.Refundable.Currency);
        if (devolvible.IsZero) return null;

        // Acá se devuelve TODO lo devolvible, y la penalidad no pinta nada — lo destapó una
        // mutación que no se puso roja. Una compensación corre cuando un paso falló a la mitad, y
        // en ese camino nunca hay penalidad que retener: si el fallo llegó antes de capturar no
        // se movió plata, y si llegó después, la saga queda Compensated ahí mismo. Retener solo
        // tiene sentido al cancelar un viaje que salió BIEN, y eso lo hace TripFlow directamente
        // (`CancelarConfirmadoAsync`). Restar acá era código que aparentaba proteger plata sin
        // ejecutarse nunca, que es peor que no tenerlo: el siguiente lo lee y confía.

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
