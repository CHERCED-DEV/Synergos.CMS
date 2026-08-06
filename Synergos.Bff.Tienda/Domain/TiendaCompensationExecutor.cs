using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Clients;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// Cómo Tienda deshace lo que Tienda hizo.
/// </summary>
/// <remarks>
/// <b>Treinta líneas, y es TODO lo que este dominio le tiene que enseñar al motor de sagas.</b>
/// El retroceso exponencial, los ocho intentos, la guarda de «armada no es pendiente», el aviso
/// una-sola-vez y las llaves deterministas vinieron gratis de <c>Bff.Core</c>. Que el segundo
/// orquestador costara esto es la medida de si la promoción valía la pena.
/// </remarks>
public sealed class TiendaCompensationExecutor : ICompensationExecutor<PurchaseSaga>
{
    private readonly TiendaCapabilities _caps;

    public TiendaCompensationExecutor(TiendaCapabilities caps) => _caps = caps;

    public async Task<Rejection?> UndoAsync(PurchaseSaga saga, Compensation pendiente, CancellationToken ct)
        => pendiente.Kind switch
        {
            TiendaCompensations.ReleaseStockHold => await Envolver(_caps.ReleaseStockAsync(pendiente.TargetId, ct)),
            TiendaCompensations.RestockItem => await RestockAsync(saga, pendiente, ct),
            TiendaCompensations.CancelOrder => await CancelOrderAsync(pendiente, ct),
            TiendaCompensations.VoidPayment => await Envolver(_caps.VoidAsync(pendiente.TargetId, ct)),
            TiendaCompensations.RefundPayment => await RefundAsync(saga, pendiente, ct),
            _ => Rejection.Invalid("tienda.unknown_compensation", $"No sé deshacer {pendiente.Kind}."),
        };

    /// <summary>
    /// Devuelve al ítem las unidades que ya se habían consumido.
    /// </summary>
    /// <remarks>
    /// <para><b>Es un ajuste y no una liberación</b>, porque el apartado ya no existe:
    /// <c>Api.Inventory</c> lo rechaza con <c>hold_already_consumed</c> y tiene razón. Deshacer un
    /// consumo es sumar unidades al total, y para eso hacen falta el ítem y la cantidad — que la
    /// saga guardó al apartar precisamente porque después no habría de dónde sacarlos.</para>
    ///
    /// <para><b>Un ajuste RELATIVO, y sin leer antes</b> (defecto #30). Esto era un
    /// leer-sumar-escribir —traer el total, sumarle la cantidad, escribir el resultado— y dos
    /// devoluciones simultáneas sobre el mismo ítem se pisaban: las dos leían 10, escribían 12 y
    /// 13, y ganaba la última. Dos unidades desaparecidas del inventario, sin excepción y sin
    /// log. Ahora se manda <i>cuánto cambió</i> y la suma la hace la capacidad dentro de su
    /// cerrojo, que es el único sitio donde puede hacerse bien.</para>
    ///
    /// <para>La llave va determinista desde la saga porque el motor reintenta hasta ocho veces y
    /// un relativo reintentado suma ocho veces. Con ella, los ocho intentos son el mismo
    /// ajuste.</para>
    /// </remarks>
    private async Task<Rejection?> RestockAsync(PurchaseSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var apartado = saga.Holds.FirstOrDefault(h => h.ItemId == pendiente.TargetId);
        if (apartado is null)
        {
            // No debería pasar: la compensación se reescribió desde un apartado de esta misma
            // saga. Si pasa, es un defecto y hay que verlo, no reintentarlo ocho veces.
            return Rejection.Invalid("tienda.restock_without_hold",
                $"La compra no tiene ningún apartado sobre el ítem {pendiente.TargetId}.");
        }

        var r = await _caps.RestockAsync(
            pendiente.TargetId, apartado.Quantity, saga.KeyFor($"restock:{pendiente.Id}"), ct);
        return r.IsOk ? null : r.Rejection;
    }

    /// <summary>
    /// Anula el pedido. Si ya estaba anulado, se da por hecha.
    /// </summary>
    /// <remarks>
    /// El mismo razonamiento que la devolución: un reintento donde la anulación <i>sí</i> había
    /// salido pero no llegó a anotarse encontraría el pedido ya cancelado, y
    /// <c>Api.Orders</c> lo rechazaría por conflicto. Tratar «ya está como lo quería dejar» como
    /// éxito es lo que evita que la compensación quede colgada sobre algo ya resuelto.
    /// </remarks>
    private async Task<Rejection?> CancelOrderAsync(Compensation pendiente, CancellationToken ct)
    {
        var pedido = await _caps.GetOrderAsync(pendiente.TargetId, ct);
        if (!pedido.IsOk) return pedido.Rejection;
        if (string.Equals(pedido.Value.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return null;

        var r = await _caps.CancelOrderAsync(pendiente.TargetId, ct);
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
    private async Task<Rejection?> RefundAsync(PurchaseSaga saga, Compensation pendiente, CancellationToken ct)
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
