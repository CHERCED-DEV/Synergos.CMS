using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="RatingStarsResolutor"/>: <c>valueNow</c>/<c>maxStars</c>/<c>ariaLabel</c> llegan
/// como <c>value</c>/<c>max</c>/<c>label</c>, y los dos primeros como números — con los alias el
/// elemento decía «0 de 5 estrellas» (D1).
/// </summary>
public sealed class RatingStarsResolutorTests
{
    private readonly ILogger<RatingStarsResolutor> _log = Substitute.For<ILogger<RatingStarsResolutor>>();

    private RatingStarsResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_valor_y_el_maximo_viajan_como_numeros_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("valueNow", "4,5"),
            ("maxStars", "5"),
            ("ariaLabel", "Valoración de los huéspedes"))).Props);

        Assert.Equal(new[] { "value", "max", "label" }, cable.Keys);
        Assert.Equal(JsonValueKind.Number, ((JsonElement)cable["value"]!).ValueKind);
        Assert.Equal(4.5m, ((JsonElement)cable["value"]!).GetDecimal());
        Assert.Equal(5, ((JsonElement)cable["max"]!).GetInt32());
        Assert.Equal("Valoración de los huéspedes", cable["label"]?.ToString());
    }

    [Fact]
    public void Lo_que_no_es_un_numero_no_viaja_y_se_anota_en_el_log()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("valueNow", "cuatro"), ("maxStars", "5.5"))).Props;

        Assert.Equal(new RatingStarsProps(null, null, null), props);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("valueNow", "4"));

        Assert.Equal(Resolutor().Resolver(elemento), Resolutor().Resolver(elemento));
    }
}
