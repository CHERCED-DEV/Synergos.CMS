using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>Cómo se deshace cada cosa que este flujo hizo.</summary>
/// <remarks>
/// Una compensación es un DATO y no una función (§0.B.18): el flujo la ANOTA en el instante en
/// que existe lo que hay que deshacer, y esta clase sólo sabe ejecutar lo anotado.
/// </remarks>
public sealed class AlquilerCompensationExecutor : ICompensationExecutor<RentalSaga>
{
    private readonly AlquilerCapabilities _caps;

    /// <summary>Construye el ejecutor.</summary>
    /// <param name="caps">Las dos capacidades.</param>
    public AlquilerCompensationExecutor(AlquilerCapabilities caps) => _caps = caps;

    /// <inheritdoc />
    public async Task<Rejection?> UndoAsync(RentalSaga saga, Compensation pending, CancellationToken ct)
        => pending.Kind switch
        {
            AlquilerCompensations.ReleaseBookingHold
                => await Envolver(_caps.ReleaseHoldAsync(pending.TargetId, ct)),
            AlquilerCompensations.CancelReservation
                => await Envolver(_caps.CancelReservationAsync(pending.TargetId, ct)),
            AlquilerCompensations.VoidPayment
                => await Envolver(_caps.VoidAsync(pending.TargetId, ct)),
            AlquilerCompensations.RefundPayment
                => await DevolverAsync(saga, pending, ct),
            _ => Rejection.Invalid("alquiler.unknown_compensation", $"No sé deshacer {pending.Kind}."),
        };

    /// <summary>
    /// Devolver lo que quede por devolver, no el total anotado.
    /// </summary>
    /// <remarks>
    /// Se relee <c>Refundable</c> de la capacidad porque el monto puede haber cambiado entre que
    /// se anotó y que el barrido llega — horas después, sin nadie al teclado. Devolver el total
    /// anotado sobre un cobro ya devuelto a medias devolvería de más.
    /// </remarks>
    private async Task<Rejection?> DevolverAsync(
        RentalSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var pago = await _caps.GetPaymentAsync(pendiente.TargetId, ct);
        if (!pago.IsOk)
        {
            return pago.Rejection;
        }

        var devolvible = Money.Of(pago.Value.Refundable.Amount, pago.Value.Refundable.Currency);
        if (devolvible.IsZero)
        {
            return null;
        }

        var r = await _caps.RefundAsync(
            pendiente.TargetId, devolvible, pendiente.Reason, saga.KeyFor($"refund:{pendiente.Id}"), ct);
        return r.IsOk ? null : r.Rejection;
    }

    private static async Task<Rejection?> Envolver<T>(Task<Result<T>> llamada)
    {
        var r = await llamada;
        return r.IsOk ? null : r.Rejection;
    }
}
