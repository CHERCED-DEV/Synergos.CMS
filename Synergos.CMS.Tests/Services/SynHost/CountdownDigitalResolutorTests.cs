using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="CountdownDigitalResolutor"/>: la fecha llega como <c>targetDate</c> —la vista
/// la mandaba como <c>endDateTime</c> (D1)—, el interruptor siempre, y el estilo con el nombre que
/// le da el elemento.
/// </summary>
public sealed class CountdownDigitalResolutorTests
{
    private readonly ILogger<CountdownDigitalResolutor> _log = Substitute.For<ILogger<CountdownDigitalResolutor>>();

    private CountdownDigitalResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_nada_y_el_elemento_muestra_los_rotulos()
    {
        // #192, caso 24: con «mostrar», un bloque sin tocar mandaba showLabels:false y ocultaba los
        // rótulos que el elemento pinta por defecto.
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props);

        Assert.Empty(cable.Keys);
    }

    [Fact]
    public void La_fecha_el_interruptor_y_el_estilo_viajan_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("endDateTime", "2026-12-31T23:59:59-05:00"),
            ("hideLabels", true),
            ("style", "flip"))).Props);

        Assert.Equal(new[] { "targetDate", "showLabels", "style" }, cable.Keys);
        Assert.Equal("2026-12-31T23:59:59-05:00", cable["targetDate"]!.ToString());
        Assert.False(((JsonElement)cable["showLabels"]!).GetBoolean());
        Assert.Equal("flip", cable["style"]!.ToString());
    }

    [Theory]
    [InlineData("digits", "plain")]
    [InlineData("Flip", "flip")]
    [InlineData("circular", "circular")]
    public void El_estilo_viaja_con_el_nombre_que_le_da_el_elemento(string delDataType, string esperado)
    {
        Assert.Equal(esperado, Resolutor().Resolver(ElementoFalso.Con(("style", delDataType))).Props.Style);
    }

    [Fact]
    public void Una_fecha_que_no_es_ISO_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("endDateTime", "el 31 de diciembre"), ("hideLabels", true))).Props;

        Assert.Equal(new CountdownDigitalProps(null, false, null), props);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("endDateTime", "2026-12-31"), ("style", "digits"));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
