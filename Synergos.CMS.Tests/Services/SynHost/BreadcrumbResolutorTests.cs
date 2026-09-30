using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="BreadcrumbResolutor"/>: el TEXTO <c>itemsJson</c> (<c>label</c>/<c>url</c>)
/// llega como la LISTA <c>items</c> (<c>label</c>/<c>href</c>) que el elemento lee — con el texto,
/// las migas hidrataban vacías (D1).
/// </summary>
public sealed class BreadcrumbResolutorTests
{
    private const string Pasos =
        """[{"label":"Inicio","url":"/"},{"label":"Tienda","href":"/tienda"},{"label":"Zapatos"}]""";

    private readonly ILogger<BreadcrumbResolutor> _log = Substitute.For<ILogger<BreadcrumbResolutor>>();

    private BreadcrumbResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Los_pasos_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", Pasos),
            ("includeStructuredData", true))).Props);

        Assert.Equal(new[] { "items", "includeStructuredData" }, cable.Keys);
        var items = (JsonElement)cable["items"]!;
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("/", items[0].GetProperty("href").GetString());
        Assert.Equal("/tienda", items[1].GetProperty("href").GetString());
        Assert.False(items[0].TryGetProperty("url", out _));
        Assert.False(items[2].TryGetProperty("href", out _));
        Assert.True(((JsonElement)cable["includeStructuredData"]!).GetBoolean());
    }

    [Fact]
    public void Un_paso_sin_texto_no_viaja_y_el_interruptor_apagado_tampoco()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", """[{"url":"/sin-texto"},{"label":"Inicio","url":"/"}]"""),
            ("includeStructuredData", false))).Props;

        Assert.Equal(new[] { new BreadcrumbStep("Inicio", "/") }, props.Items);
        Assert.Null(props.IncludeStructuredData);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("itemsJson", Pasos));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Items, Resolutor().Resolver(elemento).Props.Items);
    }
}
