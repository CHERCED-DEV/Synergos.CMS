using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="ProgressBarResolutor"/>: <c>valueNow</c>/<c>valueMax</c> (texto) llegan como
/// <c>value</c>/<c>max</c> (números) y <c>ariaLabel</c> como <c>label</c> — con los alias la barra
/// hidrataba al 0 % con «Progreso» (D1).
/// </summary>
public sealed class ProgressBarResolutorTests
{
    private readonly ILogger<ProgressBarResolutor> _log = Substitute.For<ILogger<ProgressBarResolutor>>();

    private ProgressBarResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_avance_viaja_como_numeros_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("valueNow", "3"),
            ("valueMax", "5"),
            ("ariaLabel", "Pasos completados"))).Props);

        Assert.Equal(new[] { "value", "max", "label" }, cable.Keys);
        Assert.Equal(JsonValueKind.Number, ((JsonElement)cable["value"]!).ValueKind);
        Assert.Equal(3m, ((JsonElement)cable["value"]!).GetDecimal());
        Assert.Equal(5m, ((JsonElement)cable["max"]!).GetDecimal());
        Assert.Equal("Pasos completados", cable["label"]?.ToString());
    }

    [Fact]
    public void La_coma_decimal_vale_y_lo_que_no_es_numero_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("valueNow", "42,5"), ("valueMax", "cien"))).Props;

        Assert.Equal(new ProgressBarProps(42.5m, null, null), props);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("valueNow", "3"), ("valueMax", "5"), ("ariaLabel", "Avance"));

        Assert.Equal(Resolutor().Resolver(elemento), Resolutor().Resolver(elemento));
    }
}
