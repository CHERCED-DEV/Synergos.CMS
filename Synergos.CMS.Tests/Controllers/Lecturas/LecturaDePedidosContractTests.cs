using System.Text.Json;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// Los pedidos y el seguimiento genéricos, vistos desde <c>pedidos-api.client.ts</c> («Mis compras»,
/// 014-despiece-tienda, I4).
/// </summary>
public sealed class LecturaDePedidosContractTests
{
    private sealed record Arnes(
        LecturaDePedidosController Controlador,
        IShopOrderService Pedidos,
        IOrderTrackingService Seguimiento,
        IReturnService Devoluciones);

    private static Arnes Armar(IMemberAccessGate sesion)
    {
        var pedidos = Substitute.For<IShopOrderService>();
        var seguimiento = Substitute.For<IOrderTrackingService>();
        var devoluciones = Substitute.For<IReturnService>();
        devoluciones.CanRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ShopReturnBlock.None);
        var lectura = new LecturaDePedidosDeLaTienda(pedidos, seguimiento, devoluciones, Precios(), Tienda());
        return new Arnes(new LecturaDePedidosController(new[] { lectura }, sesion), pedidos, seguimiento, devoluciones);
    }

    private static OrderTimeline EnviadoAyer(string referencia) => new(
        referencia,
        "shipped",
        new[]
        {
            new OrderTimelineStage("paid", "Pagado", true, Ayer, null),
            new OrderTimelineStage("preparing", "En preparación", true, Ayer, null),
            new OrderTimelineStage("shipped", "Enviado", false, null, "Guía 123"),
            new OrderTimelineStage("delivered", "Entregado", false, null, null),
        },
        Ayer);

    [Fact]
    public async Task Sin_sesion_mis_pedidos_son_401()
    {
        Assert.Equal(401, Estado(await Armar(SinSesion()).Controlador.DelMiembro("tienda", default)));
    }

    [Fact]
    public async Task Mis_pedidos_emiten_lo_que_lee_mis_compras_con_la_etapa_del_despacho()
    {
        var arnes = Armar(Sesion());
        arnes.Pedidos.GetOrdersByMemberAsync(MiClave, Arg.Any<CancellationToken>())
            .Returns(new[] { Pedido("ord_1", OrderStatus.Paid, MiClave, Linea("tec-audifonos-anc", "tec-audifonos-anc-negro")) });
        arnes.Seguimiento.GetTimelineAsync("ord_1", Arg.Any<CancellationToken>()).Returns(EnviadoAyer("ord_1"));

        var pedido = Wire(await arnes.Controlador.DelMiembro("tienda", default)).GetProperty("pedidos")[0];

        TieneLasClaves(pedido, "referencia", "numero", "estado", "fecha", "total", "lineas");
        TieneLasClaves(pedido.GetProperty("total"), "monto", "moneda", "texto");
        Assert.Equal("shipped", pedido.GetProperty("estado").GetString());
        var linea = pedido.GetProperty("lineas")[0];
        TieneLasClaves(linea, "id", "elementoId", "opcionId", "titulo", "cantidad", "unitario", "total", "puedeDevolverse", "motivoSinDevolucion");
        Assert.Equal("tec-audifonos-anc/tec-audifonos-anc-negro", linea.GetProperty("id").GetString());
        Assert.True(linea.GetProperty("puedeDevolverse").GetBoolean());
        Assert.Equal(JsonValueKind.Null, linea.GetProperty("motivoSinDevolucion").ValueKind);
    }

    [Fact]
    public async Task Una_linea_con_la_devolucion_abierta_dice_por_que_no_se_devuelve()
    {
        var arnes = Armar(Sesion());
        arnes.Pedidos.GetOrdersByMemberAsync(MiClave, Arg.Any<CancellationToken>())
            .Returns(new[] { Pedido("ord_1", OrderStatus.Paid, MiClave, Linea("tec-audifonos-anc")) });
        arnes.Devoluciones.CanRequestAsync("ord_1", "tec-audifonos-anc", Arg.Any<CancellationToken>())
            .Returns(ShopReturnBlock.AlreadyOpen);

        var linea = Wire(await arnes.Controlador.DelMiembro("tienda", default)).GetProperty("pedidos")[0].GetProperty("lineas")[0];

        Assert.False(linea.GetProperty("puedeDevolverse").GetBoolean());
        Assert.Equal("ya-abierta", linea.GetProperty("motivoSinDevolucion").GetString());
    }

    [Fact]
    public async Task El_seguimiento_emite_sus_etapas_hechas_la_actual_y_las_pendientes()
    {
        var arnes = Armar(SinSesion());
        arnes.Pedidos.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Pedido("ord_1", OrderStatus.Paid, null, Linea("tec-audifonos-anc")));
        arnes.Seguimiento.GetTimelineAsync("ord_1", Arg.Any<CancellationToken>()).Returns(EnviadoAyer("ord_1"));

        var seguimiento = Wire(await arnes.Controlador.Seguimiento("tienda", "ord_1", default));

        TieneLasClaves(seguimiento, "referencia", "numero", "estado", "etapaActual", "etapas");
        Assert.Equal("shipped", seguimiento.GetProperty("etapaActual").GetString());
        TieneLasClaves(seguimiento.GetProperty("etapas")[0], "clave", "titulo", "estado", "fecha", "nota");
        Assert.Equal(
            new[] { "hecha", "hecha", "actual", "pendiente" },
            seguimiento.GetProperty("etapas").EnumerateArray().Select(e => e.GetProperty("estado").GetString()));
    }

    /// <summary>La referencia es la credencial de quien compró sin cuenta; un pedido con dueño no lo lee otro miembro.</summary>
    [Fact]
    public async Task El_seguimiento_de_un_pedido_ajeno_es_403_y_el_personal_lo_lee()
    {
        foreach (var (sesion, esperado) in new[] { (Sesion(), 403), (Sesion(personal: true), 200), (SinSesion(), 200) })
        {
            var arnes = Armar(sesion);
            arnes.Pedidos.GetOrderAsync("ord_1", Arg.Any<CancellationToken>())
                .Returns(Pedido("ord_1", OrderStatus.Paid, OtraClave, Linea("tec-audifonos-anc")));

            Assert.Equal(esperado, Estado(await arnes.Controlador.Seguimiento("tienda", "ord_1", default)));
        }
    }

    [Fact]
    public async Task El_seguimiento_de_un_pedido_que_no_existe_es_404()
    {
        Assert.Equal(404, Estado(await Armar(Sesion()).Controlador.Seguimiento("tienda", "ord_no", default)));
    }
}
