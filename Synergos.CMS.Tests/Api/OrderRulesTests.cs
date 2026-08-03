using Synergos.Api.Orders.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="OrderRules"/> — líneas, totales y transiciones.</summary>
public sealed class OrderRulesTests
{
    private static Money Cop(decimal x) => Money.Of(x, "COP");

    private static OrderLine Linea(decimal precio, int cantidad = 1, string moneda = "COP")
        => new(Ref.Create("shop.producto", $"p{precio}"), cantidad, Money.Of(precio, moneda));

    private static Order Pedido(OrderStatus estado)
        => new("o1", Ref.Create("identity.member", "m-1"), new[] { Linea(10_000m) },
            Cop(10_000m), estado, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Un_pedido_que_MEZCLA_monedas_se_rechaza()
    {
        var bad = OrderRules.CheckLines(new[] { Linea(10_000m), Linea(5m, moneda: "USD") });

        Assert.Equal("orders.mixed_currencies", bad?.Code);
    }

    [Fact]
    public void Un_pedido_vacio_o_con_cantidad_cero_no_pide_nada()
    {
        Assert.Equal("orders.empty_order", OrderRules.CheckLines(Array.Empty<OrderLine>())?.Code);
        Assert.Equal("orders.bad_quantity", OrderRules.CheckLines(new[] { Linea(10m, cantidad: 0) })?.Code);
    }

    [Fact]
    public void El_total_puede_estar_por_ENCIMA_o_por_DEBAJO_de_las_lineas()
    {
        // Por debajo es un descuento; por ENCIMA es el impuesto, que Pricing SUMA al subtotal.
        // La primera versión de la regla rechazaba lo de arriba —"el impuesto ya viene dentro"—
        // y habría rechazado todo pedido con IVA. Lo destapó levantar los procesos, no un test:
        // los tests estaban escritos contra la misma suposición equivocada.
        var lineas = new[] { Linea(10_000m, 2) };   // suma 20 000

        Assert.Null(OrderRules.CheckTotal(lineas, Cop(18_000m)));   // descuento
        Assert.Null(OrderRules.CheckTotal(lineas, Cop(20_000m)));   // sin nada
        Assert.Null(OrderRules.CheckTotal(lineas, Cop(23_800m)));   // + IVA 19 %
    }

    [Fact]
    public void Un_total_negativo_o_en_otra_moneda_no_cuadra()
    {
        var lineas = new[] { Linea(10_000m) };

        Assert.Equal("orders.negative_total", OrderRules.CheckTotal(lineas, Cop(-1m))?.Code);
        Assert.Equal("orders.total_currency_mismatch", OrderRules.CheckTotal(lineas, Money.Of(5m, "USD"))?.Code);
    }

    [Fact]
    public void Un_pedido_CUMPLIDO_no_se_cancela()
    {
        // Eso es una devolución, que es otra operación y de otra capacidad.
        var bad = OrderRules.CheckTransition(Pedido(OrderStatus.Fulfilled), OrderStatus.Cancelled);

        Assert.Equal("orders.order_closed", bad?.Code);
    }

    [Fact]
    public void Un_pedido_CANCELADO_no_revive()
    {
        // Reabrirlo dejaría dos verdades sobre si el cliente debe plata.
        Assert.Equal("orders.order_closed", OrderRules.CheckTransition(Pedido(OrderStatus.Cancelled), OrderStatus.Fulfilled)?.Code);
    }

    [Fact]
    public void Repetir_la_transicion_es_conflicto_visible_y_no_silencio()
    {
        Assert.Equal("orders.already_fulfilled", OrderRules.CheckTransition(Pedido(OrderStatus.Fulfilled), OrderStatus.Fulfilled)?.Code);
    }

    [Fact]
    public void Desde_Placed_se_puede_cumplir_o_cancelar()
    {
        Assert.Null(OrderRules.CheckTransition(Pedido(OrderStatus.Placed), OrderStatus.Fulfilled));
        Assert.Null(OrderRules.CheckTransition(Pedido(OrderStatus.Placed), OrderStatus.Cancelled));
    }
}
