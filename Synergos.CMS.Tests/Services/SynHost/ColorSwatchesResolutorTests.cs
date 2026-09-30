using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="ColorSwatchesResolutor"/>: el TEXTO <c>swatchesJson</c> (<c>hex</c>/<c>name</c>)
/// llega como la LISTA <c>swatches</c> (<c>color</c>/<c>label</c>) que el elemento lee — con el
/// texto, la paleta hidrataba vacía (D1).
/// </summary>
public sealed class ColorSwatchesResolutorTests
{
    private const string Muestras =
        """[{"hex":"#1e3a8a","name":"Azul noche"},{"color":"#f97316","label":"Naranja"}]""";

    private readonly ILogger<ColorSwatchesResolutor> _log = Substitute.For<ILogger<ColorSwatchesResolutor>>();

    private ColorSwatchesResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_muestras_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("swatchesJson", Muestras),
            ("shape", "Circle"))).Props);

        Assert.Equal(new[] { "swatches", "shape" }, cable.Keys);
        var swatches = (JsonElement)cable["swatches"]!;
        Assert.Equal(2, swatches.GetArrayLength());
        Assert.Equal("#1e3a8a", swatches[0].GetProperty("color").GetString());
        Assert.Equal("Azul noche", swatches[0].GetProperty("label").GetString());
        Assert.False(swatches[0].TryGetProperty("hex", out _));
        Assert.Equal("Naranja", swatches[1].GetProperty("label").GetString());
        Assert.Equal("circle", cable["shape"]?.ToString());
    }

    [Fact]
    public void Una_muestra_sin_color_no_viaja_y_se_anota()
    {
        var swatches = Resolutor().Resolver(ElementoFalso.Con(
            ("swatchesJson", """[{"name":"Sin color"},{"hex":"#000"}]"""))).Props.Swatches;

        Assert.Equal(new[] { new ColorSwatchesItem("#000") }, swatches);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("swatchesJson", Muestras));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Swatches, Resolutor().Resolver(elemento).Props.Swatches);
    }
}
