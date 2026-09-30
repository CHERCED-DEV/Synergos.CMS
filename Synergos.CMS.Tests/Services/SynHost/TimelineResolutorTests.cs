using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="TimelineResolutor"/>: el TEXTO <c>eventsJson</c> llega como la LISTA
/// <c>events</c> (<c>description</c> → <c>body</c>) — con el alias la línea de tiempo salía vacía
/// (D1) — y la orientación, que el elemento no pinta, no viaja.
/// </summary>
public sealed class TimelineResolutorTests
{
    private const string Hitos =
        """[{"date":"2019-03-01","title":"Fundación","description":"Abrimos la primera sede.","iconKey":"flag"},{"date":"Q3 2025","title":"Segunda sede"}]""";

    private readonly ILogger<TimelineResolutor> _log = Substitute.For<ILogger<TimelineResolutor>>();

    private TimelineResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Los_hitos_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("eventsJson", Hitos),
            ("orientation", "horizontal"))).Props);

        Assert.Equal(new[] { "events" }, cable.Keys);
        var events = (JsonElement)cable["events"]!;
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal("2019-03-01", events[0].GetProperty("date").GetString());
        Assert.Equal("Abrimos la primera sede.", events[0].GetProperty("body").GetString());
        Assert.False(events[0].TryGetProperty("description", out _));
        Assert.False(events[0].TryGetProperty("iconKey", out _));
        Assert.False(events[1].TryGetProperty("body", out _));
    }

    [Fact]
    public void Un_hito_vacio_o_un_json_roto_no_viajan_y_se_anotan()
    {
        var vacio = Resolutor().Resolver(ElementoFalso.Con(
            ("eventsJson", """[{"iconKey":"flag"},{"title":"Único"}]"""))).Props;
        var roto = Resolutor().Resolver(ElementoFalso.Con(("eventsJson", "[{"))).Props;

        Assert.Equal(new[] { new TimelineEntry(Title: "Único") }, vacio.Events);
        Assert.Null(roto.Events);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("eventsJson", Hitos));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Events, Resolutor().Resolver(elemento).Props.Events);
    }
}
