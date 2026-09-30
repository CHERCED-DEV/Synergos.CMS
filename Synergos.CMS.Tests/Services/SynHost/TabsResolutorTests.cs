using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="TabsResolutor"/>: el TEXTO <c>tabsJson</c> llega como la LISTA <c>tabs</c> que
/// el elemento lee — con el alias las pestañas desaparecían al hidratar (D1).
/// </summary>
public sealed class TabsResolutorTests
{
    private const string Pestanas =
        """[{"id":"resumen","label":"Resumen","content":"Lo esencial."},{"label":"Precios","content":"Desde $120.000.","disabled":true}]""";

    private readonly ILogger<TabsResolutor> _log = Substitute.For<ILogger<TabsResolutor>>();

    private TabsResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_pestanas_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("tabsJson", Pestanas),
            ("initialTab", "resumen"))).Props);

        Assert.Equal(new[] { "tabs", "initialTab" }, cable.Keys);
        var tabs = (JsonElement)cable["tabs"]!;
        Assert.Equal(2, tabs.GetArrayLength());
        Assert.Equal("resumen", tabs[0].GetProperty("id").GetString());
        Assert.Equal("Lo esencial.", tabs[0].GetProperty("content").GetString());
        Assert.False(tabs[1].TryGetProperty("id", out _));
        Assert.False(tabs[1].TryGetProperty("disabled", out _));
        Assert.Equal("resumen", cable["initialTab"]?.ToString());
    }

    [Fact]
    public void Una_pestana_sin_rotulo_o_un_json_que_no_es_lista_no_viajan_y_se_anotan()
    {
        var sinRotulo = Resolutor().Resolver(ElementoFalso.Con(
            ("tabsJson", """[{"content":"huérfano"},{"label":"Única"}]"""))).Props;
        var noLista = Resolutor().Resolver(ElementoFalso.Con(("tabsJson", """{"label":"Sola"}"""))).Props;

        Assert.Equal(new[] { new TabsItem("Única") }, sinRotulo.Tabs);
        Assert.Null(noLista.Tabs);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("tabsJson", Pestanas));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Tabs, Resolutor().Resolver(elemento).Props.Tabs);
    }
}
