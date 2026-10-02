using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La vitrina del hub enlaza <c>/synergos/apps/&lt;slug&gt;</c> para cada vertical y arma la página
/// de cada recorrido desde otra tabla. Hoteles estaba en la primera y no en la segunda: dos páginas
/// lo enlazaban a un 404 (Synergos.CMS#187).
/// </summary>
public sealed class VitrinaDeAppsTests
{
    [Fact]
    public void Cada_vertical_que_la_vitrina_enlaza_tiene_su_recorrido_y_ningun_recorrido_sobra()
    {
        var enlaza = DevContentFiller.SlugsQueLaVitrinaEnlaza;
        var recorre = DevContentFiller.SlugsQueLaVitrinaRecorre;

        // Dos conjuntos vacíos son iguales: sin este piso, un descubrimiento roto pasaría en verde.
        Assert.NotEmpty(enlaza);
        Assert.Equal(
            enlaza.Order(StringComparer.Ordinal),
            recorre.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Soluciones enlaza <c>/{slug}</c> para cada vertical con sitio, y el sitio se busca por esa
    /// RUTA. Buscaba por <c>brandKey</c>, y Tienda (<c>ecommerce</c>) y Booking (<c>meridian</c>)
    /// salían sin enlace: el lanzador listaba 7 de 9, medido en vivo (#188).
    /// </summary>
    [Fact]
    public void Cada_vertical_que_la_vitrina_enlaza_encuentra_su_sitio_por_la_ruta_y_no_por_la_marca()
    {
        // Los siteRoots como los crea SeedVerticalSiteRoots: nombre (que da la ruta) y marca.
        var sitios = new[]
        {
            Sitio("Blogs", "blogs"), Sitio("Tienda", "ecommerce"), Sitio("Healthcare", "healthcare"),
            Sitio("Educacion", "educacion"), Sitio("Booking", "meridian"), Sitio("Eventos", "eventos"),
            Sitio("Propiedades", "propiedades"), Sitio("Hoteles", "hoteles"), Sitio("Gobierno", "gobierno"),
        };
        var urls = new DefaultShortStringHelper(new DefaultShortStringHelperConfig().WithDefault(new RequestHandlerSettings()));

        var sinSitio = DevContentFiller.SlugsQueLaVitrinaEnlaza
            .Where(slug => DevContentFiller.SitioEnLaRuta(sitios, slug, urls) is null)
            .ToList();

        Assert.NotEmpty(DevContentFiller.SlugsQueLaVitrinaEnlaza);
        Assert.True(sinSitio.Count == 0,
            "Estos verticales tienen sitio y la vitrina no lo encuentra, así que Soluciones no los "
            + "enlaza: " + string.Join(", ", sinSitio));
    }

    [Fact]
    public void Un_vertical_sin_sitio_no_se_enlaza()
    {
        var urls = new DefaultShortStringHelper(new DefaultShortStringHelperConfig().WithDefault(new RequestHandlerSettings()));

        Assert.Null(DevContentFiller.SitioEnLaRuta(new[] { Sitio("Blogs", "blogs") }, "tienda", urls));
    }

    private static IContent Sitio(string nombre, string marca)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns("siteRoot");
        var sitio = Substitute.For<IContent>();
        sitio.ContentType.Returns(tipo);
        sitio.Name.Returns(nombre);
        sitio.GetCultureName(Arg.Any<string?>()).Returns(nombre);
        sitio.GetValue<string>("brandKey").Returns(marca);
        return sitio;
    }

    // ── El parche del lanzador de Soluciones (#188): arreglar el sembrador no arregla lo sembrado ──

    private static readonly Guid Lanzador = Guid.Parse("1e39923e-cd92-4b40-8b3d-3a94c23168cf");
    private static readonly Guid Hero = Guid.Parse("0a2edb7e-8555-4044-bebc-bf0fe37662f2");
    private const string Nueve = "[{\"id\":\"tienda\",\"name\":\"Tienda\"},{\"id\":\"hoteles\",\"name\":\"Hoteles\"}]";
    private const string Siete = "[{\"id\":\"eventos\",\"name\":\"Eventos\"}]";

    private static string Pagina(string appsComoValor) =>
        "{\"layout\":{\"Umbraco.BlockGrid\":[]},\"contentData\":["
        + $"{{\"contentTypeKey\":\"{Hero}\",\"udi\":\"umb://element/a\",\"headingTitle\":\"Una solución\",\"apps\":\"no-es-del-lanzador\"}},"
        + $"{{\"contentTypeKey\":\"{Lanzador}\",\"udi\":\"umb://element/b\",\"heading\":\"Soluciones disponibles\",\"apps\":{appsComoValor}}}"
        + "],\"settingsData\":[]}";

    private static string ComoTexto(string json) => System.Text.Json.JsonSerializer.Serialize(json);

    [Fact]
    public void Sin_secciones_no_hay_nada_que_parchear()
    {
        Assert.Null(DevContentFiller.ConLaListaDelLanzador(null, Lanzador, Nueve, out var n));
        Assert.Equal(0, n);
        Assert.Equal("", DevContentFiller.ConLaListaDelLanzador("", Lanzador, Nueve, out _));
    }

    [Fact]
    public void Reemplaza_la_lista_del_lanzador_y_conserva_que_viaja_como_texto()
    {
        var despues = DevContentFiller.ConLaListaDelLanzador(Pagina(ComoTexto(Siete)), Lanzador, Nueve, out var lanzadores);

        Assert.Equal(1, lanzadores);
        var bloque = System.Text.Json.Nodes.JsonNode.Parse(despues!)!["contentData"]![1]!;
        Assert.Equal(Nueve, bloque["apps"]!.GetValue<string>());
        Assert.Equal("Soluciones disponibles", bloque["heading"]!.GetValue<string>());
    }

    [Fact]
    public void Una_lista_expandida_como_la_exporta_uSync_se_reemplaza_como_lista()
    {
        var despues = DevContentFiller.ConLaListaDelLanzador(Pagina(Siete), Lanzador, Nueve, out _);

        var apps = System.Text.Json.Nodes.JsonNode.Parse(despues!)!["contentData"]![1]!["apps"]!;
        Assert.IsType<System.Text.Json.Nodes.JsonArray>(apps);
        Assert.Equal(2, apps.AsArray().Count);
    }

    [Fact]
    public void Ningun_otro_bloque_cambia_aunque_tenga_una_propiedad_que_se_llame_igual()
    {
        var despues = DevContentFiller.ConLaListaDelLanzador(Pagina(ComoTexto(Siete)), Lanzador, Nueve, out _);

        var hero = System.Text.Json.Nodes.JsonNode.Parse(despues!)!["contentData"]![0]!;
        Assert.Equal("no-es-del-lanzador", hero["apps"]!.GetValue<string>());
        Assert.Equal("Una solución", hero["headingTitle"]!.GetValue<string>());
    }

    [Fact]
    public void Con_la_lista_ya_al_dia_devuelve_el_mismo_texto_y_no_hay_nada_que_guardar()
    {
        var antes = Pagina(ComoTexto(Nueve));

        var despues = DevContentFiller.ConLaListaDelLanzador(antes, Lanzador, Nueve, out var lanzadores);

        // El MISMO texto, no uno equivalente reserializado: el que llama decide guardar por eso.
        Assert.Same(antes, despues);
        Assert.Equal(1, lanzadores);
    }

    [Fact]
    public void Una_pagina_sin_el_lanzador_lo_dice_y_no_se_toca()
    {
        var sinLanzador = Pagina(ComoTexto(Siete)).Replace(Lanzador.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);

        var despues = DevContentFiller.ConLaListaDelLanzador(sinLanzador, Lanzador, Nueve, out var lanzadores);

        Assert.Equal(0, lanzadores);
        Assert.Same(sinLanzador, despues);
    }
}
