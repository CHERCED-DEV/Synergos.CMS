using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="ScrollTopResolutor"/>: el <c>ariaLabel</c> del editor llega como <c>label</c>,
/// que es lo que el elemento lee — con el alias el botón decía siempre «Volver arriba» (D1) — y el
/// umbral como número.
/// </summary>
public sealed class ScrollTopResolutorTests
{
    private readonly ILogger<ScrollTopResolutor> _log = Substitute.For<ILogger<ScrollTopResolutor>>();

    private ScrollTopResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con());

        Assert.Empty(SolicitudSynHost.Props(resuelto.Props));
        Assert.Null(resuelto.RespaldoHtml);
    }

    [Fact]
    public void El_umbral_la_posicion_y_el_nombre_accesible_viajan_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("scrollThreshold", "400"),
            ("position", "bottom-left"),
            ("ariaLabel", "Subir al inicio"))).Props);

        Assert.Equal(new[] { "scrollThreshold", "position", "label" }, cable.Keys);
        Assert.Equal(JsonValueKind.Number, ((JsonElement)cable["scrollThreshold"]!).ValueKind);
        Assert.Equal(400, ((JsonElement)cable["scrollThreshold"]!).GetInt32());
        Assert.Equal("bottom-left", cable["position"]?.ToString());
        Assert.Equal("Subir al inicio", cable["label"]?.ToString());
    }

    [Theory]
    [InlineData("400px")]
    [InlineData("1.000")]
    public void Un_umbral_que_no_es_un_entero_no_viaja_y_se_anota(string umbral)
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("scrollThreshold", umbral))).Props;

        Assert.Equal(new ScrollTopProps(null, null, null), props);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("scrollThreshold", "400"), ("ariaLabel", "Subir"));

        Assert.Equal(Resolutor().Resolver(elemento), Resolutor().Resolver(elemento));
    }
}
