using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="DropdownResolutor"/>: el TEXTO <c>optionsJson</c> del editor llega como la LISTA
/// <c>options</c> que el elemento lee — con el texto, todo dropdown colocado salía sin opciones (D1).
/// </summary>
public sealed class DropdownResolutorTests
{
    private readonly ILogger<DropdownResolutor> _log = Substitute.For<ILogger<DropdownResolutor>>();

    private DropdownResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    private int Anotados() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(("searchable", false))).Props));
    }

    [Fact]
    public void Las_opciones_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("triggerLabel", "País"),
            ("optionsJson", """[{"value":"co","label":"Colombia"},{"value":"mx","label":"México","href":"/mx"}]"""),
            ("selectedValue", "co"),
            ("searchable", true))).Props);

        Assert.Equal(new[] { "triggerLabel", "options", "selectedValue", "searchable" }, cable.Keys);
        var opciones = (JsonElement)cable["options"]!;
        Assert.Equal(JsonValueKind.Array, opciones.ValueKind);
        Assert.Equal(2, opciones.GetArrayLength());
        Assert.Equal("Colombia", opciones[0].GetProperty("label").GetString());
        Assert.False(opciones[0].TryGetProperty("href", out _));
        Assert.Equal("/mx", opciones[1].GetProperty("href").GetString());
        Assert.True(((JsonElement)cable["searchable"]!).GetBoolean());
    }

    [Fact]
    public void Una_opcion_con_un_solo_texto_lo_usa_para_los_dos_y_url_vale_como_href()
    {
        var opciones = Resolutor().Resolver(ElementoFalso.Con(
            ("optionsJson", """[{"label":"Ayuda","url":"/ayuda"},{"value":7}]"""))).Props.Options;

        Assert.Equal(new[] { new DropdownOption("Ayuda", "Ayuda", "/ayuda"), new DropdownOption("7", "7") }, opciones);
    }

    [Fact]
    public void Un_grupo_o_un_JSON_roto_no_viajan_y_se_anotan()
    {
        var conGrupo = Resolutor().Resolver(ElementoFalso.Con(
            ("optionsJson", """[{"group":"LATAM","options":[{"value":"co"}]},{"value":"es"}]"""))).Props.Options;
        var roto = Resolutor().Resolver(ElementoFalso.Con(("optionsJson", "[{value:"))).Props.Options;
        var objeto = Resolutor().Resolver(ElementoFalso.Con(("optionsJson", """{"value":"co"}"""))).Props.Options;

        Assert.Equal(new[] { new DropdownOption("es", "es") }, conGrupo);
        Assert.Null(roto);
        Assert.Null(objeto);
        Assert.Equal(3, Anotados());
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("optionsJson", """[{"value":"co","label":"Colombia"}]"""));

        var una = Resolutor().Resolver(elemento).Props;
        var otra = Resolutor().Resolver(elemento).Props;

        Assert.Equal(una.Options, otra.Options);
        Assert.Equal(una with { Options = null }, otra with { Options = null });
    }
}
