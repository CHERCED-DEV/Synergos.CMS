using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// La bandeja genérica, vista desde <c>mensajes-api.client.ts</c> de la tienda y de la consola
/// (014-despiece-tienda, I4).
/// </summary>
/// <remarks>
/// <c>GET api/shop/messages</c> mandaba <c>threadId</c> sin asunto, y los dos clientes exigían <c>id</c> y
/// <c>subject</c>: la tienda pintaba «no tienes mensajes» con hilos en el servidor, y la consola, una caída. Además
/// traía todos los hilos del correo, los de Blogs y los de Gobierno incluidos.
/// </remarks>
public sealed class LecturaDeMensajesContractTests
{
    private const string Tienda = "ventas@synergos.co";

    private static (LecturaDeMensajesController Controlador, IMessagingService Mensajeria) Armar(IMemberAccessGate sesion)
    {
        var mensajeria = Substitute.For<IMessagingService>();
        var pedidos = Substitute.For<IShopOrderService>();
        pedidos.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Pedido("ord_1", OrderStatus.Paid, MiClave, Linea("tec-audifonos-anc")));
        var catalogo = CatalogoDeLaTienda.Lectura(new ShopDemoCatalogSource(), new CatalogSettings(), Precios());
        var contexto = new ContextoDeMensajesDeLaTienda(pedidos, new[] { catalogo });
        return (new LecturaDeMensajesController(new[] { contexto }, mensajeria, sesion), mensajeria);
    }

    private static MessageThreadSummary Resumen(string id, string contexto)
        => new(id, contexto, new[] { Yo, Tienda }, "¿Ya salió mi pedido?", Ayer, 2);

    private static MessageThread Hilo(string id, string contexto) => new(
        id,
        contexto,
        new[] { Yo, Tienda },
        new[]
        {
            new ThreadMessage("m1", Yo, "¿Ya salió mi pedido?", Ayer.AddHours(-1)),
            new ThreadMessage("m2", Tienda, "Sale hoy en la tarde.", Ayer),
        },
        Ayer.AddHours(-1),
        Ayer);

    [Fact]
    public async Task Sin_sesion_es_401()
    {
        Assert.Equal(401, Estado(await Armar(SinSesion()).Controlador.Bandeja("tienda", default)));
    }

    [Fact]
    public async Task Trae_solo_los_hilos_de_la_tienda_con_su_asunto_y_sus_mensajes()
    {
        var (controlador, mensajeria) = Armar(Sesion());
        mensajeria.GetInboxAsync(Yo, Arg.Any<CancellationToken>()).Returns(new[]
        {
            Resumen("h-tienda", "ord_1"),
            Resumen("h-blogs", "dm"),
            Resumen("h-gov", "gov-correspondencia:RAD-2026-001"),
        });
        mensajeria.GetThreadAsync("h-tienda", Arg.Any<CancellationToken>()).Returns(Hilo("h-tienda", "ord_1"));

        var hilos = Wire(await controlador.Bandeja("tienda", default)).GetProperty("hilos");

        var hilo = Assert.Single(hilos.EnumerateArray());
        TieneLasClaves(hilo, "id", "contexto", "asunto", "contraparte", "ultimo", "fecha", "cantidad", "mensajes");
        Assert.Equal("h-tienda", hilo.GetProperty("id").GetString());
        Assert.Equal("Pedido SYN-ABCDEF12", hilo.GetProperty("asunto").GetString());
        Assert.Equal(Tienda, hilo.GetProperty("contraparte").GetString());
        TieneLasClaves(hilo.GetProperty("mensajes")[0], "id", "de", "mio", "texto", "fecha");
        Assert.Equal(
            new[] { true, false },
            hilo.GetProperty("mensajes").EnumerateArray().Select(m => m.GetProperty("mio").GetBoolean()));
    }

    [Fact]
    public async Task Un_hilo_sobre_un_producto_se_titula_con_el_producto()
    {
        var (controlador, mensajeria) = Armar(Sesion());
        mensajeria.GetInboxAsync(Yo, Arg.Any<CancellationToken>())
            .Returns(new[] { Resumen("h-1", "tienda:producto:tec-laptop-pro-14") });

        var hilo = Wire(await controlador.Bandeja("tienda", default)).GetProperty("hilos")[0];

        Assert.Equal("Laptop Pro 14\" M-series", hilo.GetProperty("asunto").GetString());
        Assert.Equal(0, hilo.GetProperty("mensajes").GetArrayLength());
    }
}
