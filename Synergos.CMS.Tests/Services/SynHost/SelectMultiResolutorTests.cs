using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="SelectMultiResolutor"/>: el TEXTO <c>optionsJson</c> llega como la LISTA
/// <c>options</c> que el elemento lee, y el tope como número — con el alias el multiselector salía
/// sin opciones (D1).
/// </summary>
public sealed class SelectMultiResolutorTests
{
    private const string Opciones =
        """[{"value":"piscina","label":"Piscina"},{"value":"gym","label":"Gimnasio","disabled":true}]""";

    private readonly ILogger<SelectMultiResolutor> _log = Substitute.For<ILogger<SelectMultiResolutor>>();

    private SelectMultiResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_opciones_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("label", "Amenidades"),
            ("optionsJson", Opciones),
            ("maxSelections", "3"))).Props);

        Assert.Equal(new[] { "label", "options", "maxSelections" }, cable.Keys);
        var options = (JsonElement)cable["options"]!;
        Assert.Equal(2, options.GetArrayLength());
        Assert.Equal("piscina", options[0].GetProperty("value").GetString());
        Assert.Equal("Gimnasio", options[1].GetProperty("label").GetString());
        Assert.True(options[1].GetProperty("disabled").GetBoolean()); // #192, caso 11: ahora viaja
        Assert.Equal(3, ((JsonElement)cable["maxSelections"]!).GetInt32());
    }

    [Fact]
    public void Un_json_roto_o_una_entrada_sin_textos_no_viaja_y_se_anota()
    {
        var roto = Resolutor().Resolver(ElementoFalso.Con(("optionsJson", "[{\"value\":"))).Props;
        var sinTextos = Resolutor().Resolver(ElementoFalso.Con(
            ("optionsJson", """[{"disabled":true},{"label":"Parqueadero"}]"""))).Props;

        Assert.Null(roto.Options);
        Assert.Equal(new[] { new SelectMultiItem("Parqueadero", "Parqueadero") }, sinTextos.Options);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("optionsJson", Opciones));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Options, Resolutor().Resolver(elemento).Props.Options);
    }

    // #192, caso 11: el elemento pinta una opción apagada y el record no la traía. Sólo viaja el
    // encendido: disabled:false es no decir nada.
    [Fact]
    public void Una_opcion_apagada_viaja_y_una_encendida_no_lleva_la_clave()
    {
        var opciones = Resolutor().Resolver(ElementoFalso.Con(
            ("optionsJson", """[{"value":"a","label":"A","disabled":true},{"value":"b","label":"B","disabled":false},{"value":"c","label":"C","disabled":"true"}]"""))).Props.Options!;

        Assert.Equal(new bool?[] { true, null, true }, opciones.Select(o => o.Disabled).ToArray());
    }
}
