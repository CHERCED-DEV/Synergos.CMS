using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="ColorPickerResolutor"/>: el TEXTO <c>paletteJson</c> llega como la LISTA
/// <c>palette</c> de colores hex —la vista mandaba el texto y el selector pintaba la paleta de
/// fábrica (D1)— y lo que no es un hex no viaja.
/// </summary>
public sealed class ColorPickerResolutorTests
{
    private readonly ILogger<ColorPickerResolutor> _log = Substitute.For<ILogger<ColorPickerResolutor>>();

    private ColorPickerResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    private int Anotados() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void La_paleta_viaja_como_lista_de_hex_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("label", "Color del botón"),
            ("initialColor", "#ff6600"),
            ("paletteJson", """["#ff6600", " #0066FF ", "f60"]"""))).Props);

        Assert.Equal(new[] { "label", "initialColor", "palette" }, cable.Keys);
        var paleta = (JsonElement)cable["palette"]!;
        Assert.Equal(new[] { "#ff6600", "#0066FF", "f60" }, paleta.EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public void Lo_que_no_es_un_hex_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("initialColor", "naranja"),
            ("paletteJson", """["#ff6600", "rojo", "#12345", 42, {"hex":"#fff"}]"""))).Props;

        Assert.Equal(new ColorPickerProps(null, null, props.Palette), props);
        Assert.Equal(new[] { "#ff6600" }, props.Palette);
        Assert.Equal(5, Anotados());
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("paletteJson", """["#abc","#123456"]"""));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Palette, Resolutor().Resolver(elemento).Props.Palette);
    }
}
