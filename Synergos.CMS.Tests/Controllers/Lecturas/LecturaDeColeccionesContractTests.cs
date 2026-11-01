using System.Text.Json;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// La lista de deseos genérica, vista desde <c>deseos-api.client.ts</c> (014-despiece-tienda, I4), con cada producto
/// leído del catálogo de la tienda.
/// </summary>
public sealed class LecturaDeColeccionesContractTests
{
    private static (LecturaDeColeccionesController Controlador, IUserCollection Colecciones) Armar(IMemberAccessGate sesion)
    {
        var colecciones = Substitute.For<IUserCollection>();
        colecciones.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new UserCollectionItem(Yo, "wishlist", "tec-laptop-pro-14", Ayer),
                new UserCollectionItem(Yo, "wishlist", "ya-no-existe", Ayer),
            });
        var catalogo = CatalogoDeLaTienda.Lectura(new ShopDemoCatalogSource(), new CatalogSettings(), Precios());
        var lectura = new LecturaDeColeccionesDeLaTienda(colecciones, new[] { catalogo });
        return (new LecturaDeColeccionesController(new[] { lectura }, sesion), colecciones);
    }

    [Fact]
    public async Task Sin_sesion_es_401()
    {
        Assert.Equal(401, Estado(await Armar(SinSesion()).Controlador.Coleccion("tienda", "wishlist", default)));
    }

    /// <summary>Lo guardado que el catálogo ya no tiene sale sin elemento: se dice que estaba, no se inventa.</summary>
    [Fact]
    public async Task Trae_cada_producto_del_catalogo_y_lo_que_ya_no_esta_sin_inventarlo()
    {
        var coleccion = Wire(await Armar(Sesion()).Controlador.Coleccion("tienda", "wishlist", default));

        TieneLasClaves(coleccion, "coleccion", "elementos");
        var primero = coleccion.GetProperty("elementos")[0];
        TieneLasClaves(primero, "id", "guardado", "elemento");
        TieneLasClaves(primero.GetProperty("elemento"), "id", "titulo", "imagen", "precio");
        Assert.Equal(5_200_000m, primero.GetProperty("elemento").GetProperty("precio").GetProperty("monto").GetDecimal());
        Assert.Equal(JsonValueKind.Null, coleccion.GetProperty("elementos")[1].GetProperty("elemento").ValueKind);
    }

    /// <summary>El dueño es el correo de la SESIÓN: no hay parámetro por donde pedir la lista de otra persona.</summary>
    [Fact]
    public async Task La_lista_es_la_del_correo_de_la_sesion()
    {
        var (controlador, colecciones) = Armar(Sesion());

        await controlador.Coleccion("tienda", " wishlist ", default);

        await colecciones.Received(1).GetAsync(Yo, "wishlist", Arg.Any<CancellationToken>());
    }
}
