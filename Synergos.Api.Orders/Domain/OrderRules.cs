using Synergos.Core;

namespace Synergos.Api.Orders.Domain;

/// <summary>Lo que el pedido rechaza <b>solo</b>.</summary>
public static class OrderRules
{
    public const string CodePrefix = "orders";

    public const int MaxLines = 200;

    /// <summary>Si las líneas que llegan forman un pedido válido.</summary>
    public static Rejection? CheckLines(IReadOnlyList<OrderLine> lines)
    {
        if (lines.Count == 0)
        {
            return Rejection.Invalid($"{CodePrefix}.empty_order", "Un pedido sin líneas no pide nada.");
        }
        if (lines.Count > MaxLines)
        {
            return Rejection.Invalid($"{CodePrefix}.too_many_lines", $"Hasta {MaxLines} líneas.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return Rejection.Invalid($"{CodePrefix}.bad_quantity", "Toda línea necesita cantidad mayor que cero.");
        }
        if (lines.Any(l => l.UnitPrice.IsNegative))
        {
            return Rejection.Invalid($"{CodePrefix}.negative_price", "Una línea no puede tener precio negativo.");
        }

        var monedas = lines.Select(l => l.UnitPrice.Currency).Distinct(StringComparer.Ordinal).ToList();
        return monedas.Count > 1
            ? Rejection.Invalid($"{CodePrefix}.mixed_currencies",
                $"El pedido mezcla {string.Join(" y ", monedas)}. Hace falta una tasa, y adivinarla en silencio es peor que fallar.")
            : null;
    }

    /// <summary>
    /// Si el total declarado es plausible.
    /// </summary>
    /// <remarks>
    /// <para><b>El total NO se calcula acá, y tampoco se acota por arriba.</b> Lo compone
    /// <c>Api.Pricing</c> con impuestos y descuentos que este servicio no conoce, y el impuesto
    /// se SUMA al subtotal: un pedido de 200 000 con IVA del 19 % tiene un total de 238 000.</para>
    ///
    /// <para>La primera versión de esta regla rechazaba cualquier total mayor que la suma de las
    /// líneas, con el argumento —escrito en un comentario— de que "el impuesto ya viene dentro
    /// de lo que Pricing calculó". Era falso, y lo destapó levantar los procesos: registrar un
    /// pedido con el total de una cotización real devolvía <c>total_over_lines</c>. La regla
    /// habría rechazado <b>todo pedido con impuesto</b>. Los tests no la atraparon porque los
    /// escribí contra la misma suposición equivocada.</para>
    ///
    /// <para>Lo que queda es lo que Orders SÍ puede saber sin conocer la tabla de impuestos: que
    /// el total no sea negativo y que vaya en la misma moneda que las líneas. Validar de más una
    /// regla que no se puede evaluar bien es peor que no validarla: rechaza entradas legítimas y
    /// da la sensación de que algo se está comprobando.</para>
    /// </remarks>
    public static Rejection? CheckTotal(IReadOnlyList<OrderLine> lines, Money total)
    {
        if (total.IsNegative)
        {
            return Rejection.Invalid($"{CodePrefix}.negative_total", "El total no puede ser negativo.");
        }

        var moneda = lines[0].UnitPrice.Currency;
        return string.Equals(total.Currency, moneda, StringComparison.Ordinal)
            ? null
            : Rejection.Invalid($"{CodePrefix}.total_currency_mismatch",
                $"El total va en {total.Currency} y las líneas en {moneda}.");
    }

    /// <summary>Si la transición pedida es legal.</summary>
    /// <remarks>
    /// Las transiciones son explícitas y cortas a propósito. Un pedido cumplido no se cancela —
    /// eso es una devolución, que es otra operación y de otra capacidad— y un pedido cancelado
    /// no revive: reabrirlo dejaría dos verdades sobre si el cliente debe plata.
    /// </remarks>
    public static Rejection? CheckTransition(Order order, OrderStatus destino)
    {
        if (order.Status == destino)
        {
            return Rejection.Conflict($"{CodePrefix}.already_{destino.ToString().ToLowerInvariant()}",
                $"El pedido ya estaba {destino}.");
        }
        return order.IsClosed
            ? Rejection.Conflict($"{CodePrefix}.order_closed",
                $"El pedido está {order.Status} y no admite más cambios. Una devolución no es una cancelación.")
            : null;
    }
}
