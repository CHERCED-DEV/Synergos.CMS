using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Clients;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>
/// Cómo Eventos deshace lo que Eventos hizo.
/// </summary>
/// <remarks>
/// <b>Veinte líneas, y es TODO lo que este dominio le tiene que enseñar al motor.</b> El retroceso
/// exponencial, los ocho intentos, la guarda de «armada no es pendiente», el aviso una-sola-vez y
/// las llaves deterministas vinieron gratis de <c>Bff.Core</c>. Que el TERCER orquestador costara
/// esto —y sin tocar ni una línea del motor— es la medida de si la promoción valía la pena.
/// </remarks>
public sealed class EventosCompensationExecutor : ICompensationExecutor<TicketingSaga>
{
    private readonly EventosCapabilities _caps;

    public EventosCompensationExecutor(EventosCapabilities caps) => _caps = caps;

    public async Task<Rejection?> UndoAsync(TicketingSaga saga, Compensation pendiente, CancellationToken ct)
        => pendiente.Kind switch
        {
            EventosCompensations.ReleaseSeatHold => await Envolver(_caps.ReleaseAforoAsync(pendiente.TargetId, ct)),
            EventosCompensations.RestockSeats => await RestockAsync(saga, pendiente, ct),
            EventosCompensations.VoidPayment => await Envolver(_caps.VoidAsync(pendiente.TargetId, ct)),
            EventosCompensations.RefundPayment => await RefundAsync(saga, pendiente, ct),
            _ => Rejection.Invalid("eventos.unknown_compensation", $"No sé deshacer {pendiente.Kind}."),
        };

    /// <summary>
    /// Devuelve al pozo las unidades de aforo que ya se habían consumido.
    /// </summary>
    /// <remarks>
    /// <para><b>Es un ajuste y no una liberación</b>, porque el apartado ya no existe. Deshacer un
    /// consumo es sumar unidades al total, y para eso hacen falta el pozo y la cantidad — que la
    /// saga guardó al apartar precisamente porque después no habría de dónde sacarlos.</para>
    ///
    /// <para><b>Un ajuste RELATIVO, y sin leer antes</b> (defecto #30): dos devoluciones
    /// simultáneas sobre el mismo pozo se pisarían si esto fuera leer-sumar-escribir. Se manda
    /// <i>cuánto cambió</i> y la suma la hace la capacidad dentro de su cerrojo, que es el único
    /// sitio donde puede hacerse bien. La llave va determinista desde la saga porque el motor
    /// reintenta hasta ocho veces y un relativo reintentado suma ocho.</para>
    /// </remarks>
    private async Task<Rejection?> RestockAsync(TicketingSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var apartado = saga.Holds.FirstOrDefault(h => h.ItemId == pendiente.TargetId);
        if (apartado is null)
        {
            // No debería pasar: la compensación se reescribió desde un apartado de esta misma
            // saga. Si pasa, es un defecto y hay que verlo, no reintentarlo ocho veces.
            return Rejection.Invalid("eventos.restock_without_hold",
                $"La compra no tiene ningún apartado sobre el pozo {pendiente.TargetId}.");
        }

        var r = await _caps.RestockAforoAsync(
            pendiente.TargetId, apartado.Quantity, saga.KeyFor($"restock:{pendiente.Id}"), ct);
        return r.IsOk ? null : r.Rejection;
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
    private async Task<Rejection?> RefundAsync(TicketingSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var pago = await _caps.GetPaymentAsync(pendiente.TargetId, ct);
        if (!pago.IsOk) return pago.Rejection;

        var devolvible = Money.Of(pago.Value.Refundable.Amount, pago.Value.Refundable.Currency);
        if (devolvible.IsZero) return null;

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
