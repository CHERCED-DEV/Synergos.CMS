using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Proxies;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="BreadcrumbResolutor"/>: el TEXTO <c>itemsJson</c> (<c>label</c>/<c>url</c>)
/// llega como la LISTA <c>items</c> (<c>label</c>/<c>href</c>) que el elemento lee — con el texto,
/// las migas hidrataban vacías (D1) —, y el JSON-LD <c>BreadcrumbList</c> del interruptor
/// <c>includeStructuredData</c> lo emite el CMS con esos mismos pasos (Synergos.UI#90).
/// </summary>
public sealed class BreadcrumbResolutorTests
{
    private const string Pasos =
        """[{"label":"Inicio","url":"/"},{"label":"Tienda","href":"/tienda"},{"label":"Zapatos"}]""";

    private readonly ILogger<BreadcrumbResolutor> _log = Substitute.For<ILogger<BreadcrumbResolutor>>();

    private BreadcrumbResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave_ni_JSON_LD()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(("includeStructuredData", true)));

        Assert.Empty(SolicitudSynHost.Props(resuelto.Props));
        Assert.Null(resuelto.DatosEstructurados);
    }

    [Fact]
    public void Los_pasos_viajan_como_lista_con_los_nombres_que_lee_el_elemento_y_el_interruptor_no_viaja()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", Pasos),
            ("includeStructuredData", true))).Props);

        Assert.Equal(new[] { "items" }, cable.Keys);
        var items = (JsonElement)cable["items"]!;
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("/", items[0].GetProperty("href").GetString());
        Assert.Equal("/tienda", items[1].GetProperty("href").GetString());
        Assert.False(items[0].TryGetProperty("url", out _));
        Assert.False(items[2].TryGetProperty("href", out _));
    }

    [Fact]
    public void Encendido_el_JSON_LD_es_un_BreadcrumbList_con_los_mismos_pasos_y_el_ultimo_sin_enlace()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", """[{"label":"Inicio","url":"/"},{"label":"Tienda","url":"/tienda"},{"label":"Zapatos","url":"/tienda/zapatos"}]"""),
            ("includeStructuredData", true)));

        var ld = JsonDocument.Parse(resuelto.DatosEstructurados!).RootElement;
        Assert.Equal("https://schema.org", ld.GetProperty("@context").GetString());
        Assert.Equal("BreadcrumbList", ld.GetProperty("@type").GetString());
        var lista = ld.GetProperty("itemListElement").EnumerateArray().ToList();
        Assert.Equal(new[] { "Inicio", "Tienda", "Zapatos" }, lista.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(new[] { 1, 2, 3 }, lista.Select(p => p.GetProperty("position").GetInt32()));
        Assert.All(lista, p => Assert.Equal("ListItem", p.GetProperty("@type").GetString()));
        Assert.Equal("/", lista[0].GetProperty("item").GetString());
        Assert.Equal("/tienda", lista[1].GetProperty("item").GetString());
        // El último es la página actual: el elemento le quita el enlace, y el JSON-LD también.
        Assert.False(lista[2].TryGetProperty("item", out _));
    }

    [Fact]
    public void Un_paso_sin_texto_no_viaja_y_con_el_interruptor_apagado_no_hay_JSON_LD()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", """[{"url":"/sin-texto"},{"label":"Inicio","url":"/"}]"""),
            ("includeStructuredData", false)));

        Assert.Equal(new[] { new BreadcrumbStep("Inicio", "/") }, resuelto.Props.Items);
        Assert.Null(resuelto.DatosEstructurados);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("itemsJson", Pasos), ("includeStructuredData", true));

        var primero = Resolutor().Resolver(elemento);
        var segundo = Resolutor().Resolver(elemento);

        Assert.Equal(primero.Props.Items, segundo.Props.Items);
        Assert.Equal(primero.DatosEstructurados, segundo.DatosEstructurados);
    }

    /// <summary>
    /// Lo que pide el ticket, por el camino de la vista: resolver → <see cref="SolicitudSynHost"/>
    /// → el emitter REAL. El HTML trae el <c>ld+json</c> FUERA del tag —dentro, la hidratación lo
    /// borra— y sus pasos son los que viajan en el <c>config</c>.
    /// </summary>
    [Fact]
    public async Task El_HTML_emitido_trae_el_JSON_LD_junto_al_tag_con_los_pasos_del_config()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", """[{"label":"Inicio","url":"/"},{"label":"</script><b>Tienda","url":"/tienda"},{"label":"Zapatos"}]"""),
            ("includeStructuredData", true)));

        var html = (await new DefaultSynHostEmitter(new FakeBundleRegistryClient())
            .EmitAsync(SolicitudSynHost.Para(resuelto, null, CultureInfo.GetCultureInfo("es-CO")))).ElementHtml;

        var scripts = Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>");
        Assert.Single(scripts);
        Assert.True(
            html.IndexOf("</synergos-breadcrumb>", StringComparison.Ordinal) < scripts[0].Index,
            $"El JSON-LD tiene que ir después del tag, no dentro: {html}");

        var pasosDelConfig = SolicitudSynHostTests.ConfigEmitido(html).GetProperty("items").EnumerateArray().ToList();
        var pasosDelLd = JsonDocument.Parse(scripts[0].Groups[1].Value).RootElement
            .GetProperty("itemListElement").EnumerateArray().ToList();
        Assert.Equal(
            pasosDelConfig.Select(p => p.GetProperty("label").GetString()),
            pasosDelLd.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(
            pasosDelConfig.SkipLast(1).Select(p => p.GetProperty("href").GetString()),
            pasosDelLd.SkipLast(1).Select(p => p.GetProperty("item").GetString()));
    }
}
