using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// El listado y la ficha genéricos del catálogo, vistos desde <c>catalogo-api.client.ts</c> (014-despiece-tienda, I4),
/// sobre el catálogo de muestra de la tienda.
/// </summary>
public sealed class LecturaDelCatalogoContractTests
{
    private static LecturaDelCatalogoController Controlador(string? query = null)
    {
        var controlador = new LecturaDelCatalogoController(
            new[] { CatalogoDeLaTienda.Lectura(new ShopDemoCatalogSource(), new CatalogSettings(), Precios()) });
        var http = new DefaultHttpContext();
        if (query is not null)
        {
            http.Request.QueryString = new QueryString(query);
        }

        controlador.ControllerContext = new ControllerContext { HttpContext = http };
        return controlador;
    }

    [Fact]
    public async Task El_listado_emite_la_tarjeta_las_facetas_y_la_pagina()
    {
        var listado = Wire(await Controlador().Listado("tienda", null, null, null, null, default));

        TieneLasClaves(listado, "elementos", "facetas", "total", "pagina", "porPagina");
        var elemento = listado.GetProperty("elementos")[0];
        TieneLasClaves(elemento, "id", "titulo", "imagen", "precio", "calificacion", "disponibles", "atributos");
        TieneLasClaves(elemento.GetProperty("precio"), "monto", "moneda", "texto");
        TieneLasClaves(elemento.GetProperty("calificacion"), "promedio", "cantidad");
        TieneLasClaves(elemento.GetProperty("atributos"), "marca", "categoria");
        var faceta = listado.GetProperty("facetas").EnumerateArray().Single(f => f.GetProperty("campo").GetString() == "category");
        TieneLasClaves(faceta, "campo", "titulo", "tipo", "valores");
        TieneLasClaves(faceta.GetProperty("valores")[0], "valor", "etiqueta", "cantidad");
        Assert.Equal(1, listado.GetProperty("pagina").GetInt32());
        Assert.Equal(12, listado.GetProperty("porPagina").GetInt32());
    }

    /// <summary>
    /// Misma fuente y mismo índice que la ruta interina: filtrar por el campo de la faceta (varios valores son la unión) y
    /// ordenar dan los mismos productos, en el mismo orden, que <c>GET api/shop/search</c>.
    /// </summary>
    [Fact]
    public async Task Filtrar_y_ordenar_da_lo_mismo_que_la_ruta_interina()
    {
        var generico = Wire(await Controlador("?filtro.category=Hogar,Deportes").Listado("tienda", null, "price-asc", null, null, default));
        var interino = Wire(await Interina().Search(null, "Hogar,Deportes", null, null, "price-asc", null, null, default));

        var ids = generico.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.NotEmpty(ids);
        Assert.Equal(interino.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("id").GetString()), ids);
        Assert.Equal(interino.GetProperty("total").GetInt32(), generico.GetProperty("total").GetInt32());
        Assert.All(
            generico.GetProperty("elementos").EnumerateArray(),
            e => Assert.Contains(e.GetProperty("atributos").GetProperty("categoria").GetString(), new[] { "Hogar", "Deportes" }));
    }

    [Fact]
    public async Task La_ficha_emite_el_elemento_sus_opciones_y_sus_preguntas()
    {
        var ficha = Wire(await Controlador().Ficha("tienda", "tec-laptop-pro-14", default));

        TieneLasClaves(ficha, "elemento", "descripcion", "imagenes", "opciones", "preguntas");
        Assert.Equal("tec-laptop-pro-14", ficha.GetProperty("elemento").GetProperty("id").GetString());
        Assert.Equal(3, ficha.GetProperty("imagenes").GetArrayLength());
        TieneLasClaves(ficha.GetProperty("opciones")[0], "id", "titulo", "precio", "disponibles");
        TieneLasClaves(ficha.GetProperty("preguntas")[0], "autor", "pregunta", "respuesta", "fecha");
        Assert.Equal(JsonValueKind.Null, ficha.GetProperty("preguntas")[1].GetProperty("respuesta").ValueKind);
    }

    [Fact]
    public async Task Un_vertical_sin_catalogo_y_una_ficha_que_no_existe_son_404()
    {
        Assert.Equal(404, Estado(await Controlador().Listado("eventos", null, null, null, null, default)));
        Assert.Equal(404, Estado(await Controlador().Ficha("tienda", "no-existe", default)));
    }

    private static ShopCatalogController Interina() => new(
        new StubProductCatalogProvider(),
        Substitute.For<IShopOrderService>(),
        Precios(),
        Substitute.For<IUserCollection>(),
        Substitute.For<IOrderTrackingService>(),
        Substitute.For<IReturnService>(),
        Substitute.For<IMessagingService>(),
        SinSesion(),
        Substitute.For<IShopQuery>(),
        Substitute.For<ICatalogSocialProof>());
}
