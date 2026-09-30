using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="RangeSliderResolutor"/>: <c>minValue</c>/<c>maxValue</c>/<c>initialValue</c>
/// llegan como los NÚMEROS <c>min</c>/<c>max</c>/<c>high</c> que el elemento lee — con los alias el
/// rango del editor salía de 0 a 100 (D1).
/// </summary>
public sealed class RangeSliderResolutorTests
{
    private readonly ILogger<RangeSliderResolutor> _log = Substitute.For<ILogger<RangeSliderResolutor>>();

    private RangeSliderResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_rango_viaja_como_numeros_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("label", "Precio por noche"),
            ("minValue", "50000"),
            ("maxValue", "500000"),
            ("step", "10000"),
            ("initialValue", "250000"))).Props);

        Assert.Equal(new[] { "label", "min", "max", "step", "high" }, cable.Keys);
        Assert.Equal("Precio por noche", cable["label"]?.ToString());
        foreach (var (clave, esperado) in new[] { ("min", 50000), ("max", 500000), ("step", 10000), ("high", 250000) })
        {
            Assert.Equal(JsonValueKind.Number, ((JsonElement)cable[clave]!).ValueKind);
            Assert.Equal(esperado, ((JsonElement)cable[clave]!).GetInt32());
        }
    }

    [Theory]
    [InlineData("500.000")]
    [InlineData("2,5")]
    [InlineData("cien")]
    public void Lo_que_no_es_un_entero_de_solo_digitos_no_viaja_y_se_anota(string escrito)
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("maxValue", escrito))).Props;

        Assert.Null(props.Max);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("minValue", "0"), ("maxValue", "100"), ("initialValue", "40"));

        Assert.Equal(Resolutor().Resolver(elemento), Resolutor().Resolver(elemento));
    }
}
