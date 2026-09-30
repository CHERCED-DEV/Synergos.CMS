using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="MapPinResolutor"/>: el centro y el zoom llegan como NÚMEROS y el TEXTO
/// <c>pinsJson</c> como la LISTA <c>pins</c> —la vista mandaba texto y el mapa salía en el centro
/// por defecto y sin pines (D1)—, todo con la lectura es-CO del lector.
/// </summary>
public sealed class MapPinResolutorTests
{
    private readonly ILogger<MapPinResolutor> _log = Substitute.For<ILogger<MapPinResolutor>>();

    private MapPinResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    private int Anotados() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_centro_el_zoom_y_los_pines_viajan_como_numeros_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("centerLat", "4,7110"),
            ("centerLng", "-74.0721"),
            ("zoomLevel", "13"),
            ("pinsJson", """[{"lat":4.6097,"lng":-74.0817,"title":"Oficina Bogotá","description":"Piso 12"},{"lat":"6,2518","lng":"-75.5636","title":"Medellín"}]"""))).Props);

        Assert.Equal(new[] { "centerLat", "centerLng", "zoomLevel", "pins" }, cable.Keys);
        Assert.Equal(4.711m, ((JsonElement)cable["centerLat"]!).GetDecimal());
        Assert.Equal(-74.0721m, ((JsonElement)cable["centerLng"]!).GetDecimal());
        Assert.Equal(13, ((JsonElement)cable["zoomLevel"]!).GetInt32());
        var pins = (JsonElement)cable["pins"]!;
        Assert.Equal(-74.0817m, pins[0].GetProperty("lng").GetDecimal());
        Assert.Equal(6.2518m, pins[1].GetProperty("lat").GetDecimal());
        Assert.Equal("Oficina Bogotá", pins[0].GetProperty("label").GetString());
        Assert.False(pins[0].TryGetProperty("title", out _));
    }

    [Fact]
    public void Un_pin_sin_coordenadas_legibles_no_viaja_y_se_anota_una_vez()
    {
        var pins = Resolutor().Resolver(ElementoFalso.Con(
            ("pinsJson", """[{"lat":"4.711","lng":-74.07},{"title":"Sin coordenadas"},{"lat":4.6,"lng":-74.1}]"""))).Props.Pins;

        Assert.Equal(new[] { new MapPinItem(4.6m, -74.1m) }, pins);
        Assert.Equal(2, Anotados());
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("pinsJson", """[{"lat":4.6,"lng":-74.1}]"""), ("zoomLevel", "12"));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Pins, Resolutor().Resolver(elemento).Props.Pins);
    }
}
