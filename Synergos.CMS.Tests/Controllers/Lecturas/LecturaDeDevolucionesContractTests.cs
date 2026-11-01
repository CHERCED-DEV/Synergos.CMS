using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// Las devoluciones genéricas de un pedido, vistas desde <c>devoluciones-api.client.ts</c> (014-despiece-tienda, I4).
/// </summary>
public sealed class LecturaDeDevolucionesContractTests
{
    private static (LecturaDeDevolucionesController Controlador, IShopOrderService Pedidos, IReturnService Devoluciones) Armar(
        IMemberAccessGate sesion)
    {
        var pedidos = Substitute.For<IShopOrderService>();
        var devoluciones = Substitute.For<IReturnService>();
        var lectura = new LecturaDeDevolucionesDeLaTienda(pedidos, devoluciones, Precios(), Tienda());
        return (new LecturaDeDevolucionesController(new[] { lectura }, sesion), pedidos, devoluciones);
    }

    private static ShopReturnCase Caso(string rma, ShopReturnStatus estado) => new(
        rma, "ord_1", "tec-audifonos-anc", "Audífonos Inalámbricos ANC", 1, 680_000m, "COP", "Llegó con un defecto",
        estado, Ayer, Ayer, estado == ShopReturnStatus.Refunded ? "Devuelto a la tarjeta" : null);

    /// <summary>
    /// Los cinco estados de la máquina (solicitada → aprobada/rechazada → recibida → reembolsada) con su nombre, no con
    /// los cuatro de la pantalla de antes («abierto», «en-revision»…), que eran de la tienda.
    /// </summary>
    [Fact]
    public async Task Emiten_cada_devolucion_con_su_estado_neutro()
    {
        var (controlador, pedidos, devoluciones) = Armar(Sesion());
        pedidos.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Pedido("ord_1", OrderStatus.Paid, MiClave, Linea("tec-audifonos-anc")));
        devoluciones.GetForOrderAsync("ord_1", Arg.Any<CancellationToken>())
            .Returns(new[] { Caso("rma-1", ShopReturnStatus.Requested), Caso("rma-2", ShopReturnStatus.Refunded) });

        var lectura = Wire(await controlador.DelPedido("tienda", "ord_1", default));

        TieneLasClaves(lectura, "referencia", "devoluciones");
        var primera = lectura.GetProperty("devoluciones")[0];
        TieneLasClaves(primera, "id", "linea", "titulo", "cantidad", "monto", "motivo", "estado", "solicitada", "actualizada", "nota");
        TieneLasClaves(primera.GetProperty("monto"), "monto", "moneda", "texto");
        Assert.Equal(
            new[] { "solicitada", "reembolsada" },
            lectura.GetProperty("devoluciones").EnumerateArray().Select(d => d.GetProperty("estado").GetString()));
    }

    [Fact]
    public async Task Las_de_un_pedido_ajeno_son_403_y_las_de_uno_que_no_existe_404()
    {
        var (controlador, pedidos, _) = Armar(Sesion());
        pedidos.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Pedido("ord_1", OrderStatus.Paid, OtraClave, Linea("tec-audifonos-anc")));

        Assert.Equal(403, Estado(await controlador.DelPedido("tienda", "ord_1", default)));
        Assert.Equal(404, Estado(await controlador.DelPedido("tienda", "ord_no", default)));
    }
}
