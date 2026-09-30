using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="CountdownClockResolutor"/>: la fecha llega como <c>targetDate</c> —la vista la
/// mandaba como <c>endDateTime</c> y el reloj decía «Fecha del evento no disponible» (D1)— y la
/// plantilla <c>labelFormat</c>, que el elemento no implementa, no viaja.
/// </summary>
public sealed class CountdownClockResolutorTests
{
    private readonly ILogger<CountdownClockResolutor> _log = Substitute.For<ILogger<CountdownClockResolutor>>();

    private CountdownClockResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void La_fecha_viaja_como_targetDate_y_la_plantilla_no_viaja()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("endDateTime", "2026-12-31T23:59:59Z"),
            ("labelFormat", "Quedan {days} días y {hours} horas"))).Props);

        Assert.Equal(new[] { "targetDate" }, cable.Keys);
        Assert.Equal("2026-12-31T23:59:59Z", cable["targetDate"]!.ToString());
    }

    [Fact]
    public void Una_fecha_que_no_es_ISO_no_viaja_y_se_anota()
    {
        Assert.Null(Resolutor().Resolver(ElementoFalso.Con(("endDateTime", "31/12/2026"))).Props.TargetDate);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("endDateTime", "2026-12-31"));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
