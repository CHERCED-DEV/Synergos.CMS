using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="TimelineHorizontalResolutor"/>: el TEXTO <c>eventsJson</c> llega como la LISTA
/// <c>items</c> que el elemento lee, con <c>time</c> en cada ítem —con el texto y <c>date</c>, la
/// agenda de <c>/eventos/</c> pintaba vacía (D1)—.
/// </summary>
public sealed class TimelineHorizontalResolutorTests
{
    private readonly ILogger<TimelineHorizontalResolutor> _log = Substitute.For<ILogger<TimelineHorizontalResolutor>>();

    private TimelineHorizontalResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    private int Anotaciones() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    [Fact] // empty: sin ítems no viaja lista, y el snap apagado viaja como lo dejó el editor
    public void Sin_items_no_viaja_lista()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con()).Props;

        Assert.Null(props.Items);
        Assert.False(props.SnapEnabled);
    }

    [Fact] // happy: los ítems viajan con los nombres que lee el elemento
    public void Los_items_viajan_con_time_title_track_y_description()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("eventsJson", """[{"time":"09:00","title":"Apertura","track":"Sala A","description":"Bienvenida"},{"time":"10:30","title":"Panel"}]"""),
            ("snapEnabled", true))).Props;

        Assert.Equal(
            new[] { new TimelineHorizontalItem("09:00", "Apertura", "Sala A", "Bienvenida"), new TimelineHorizontalItem("10:30", "Panel") },
            props.Items);
        Assert.True(props.SnapEnabled);
        Assert.Equal(0, Anotaciones());
    }

    [Fact] // filter: «date» —como lo escribe la demo de /eventos/— se lee como la hora
    public void Date_se_lee_como_la_hora()
    {
        var items = Resolutor().Resolver(ElementoFalso.Con(
            ("eventsJson", """[{"date":"09:00","title":"Apertura","description":"Bienvenida"}]"""))).Props.Items;

        Assert.Equal(new[] { new TimelineHorizontalItem("09:00", "Apertura", null, "Bienvenida") }, items);
    }

    [Fact] // filter: un ítem sin hora o sin título no viaja y se anota
    public void Un_item_sin_hora_o_sin_titulo_no_viaja_y_se_anota()
    {
        var items = Resolutor().Resolver(ElementoFalso.Con(
            ("eventsJson", """[{"title":"Sin hora"},{"time":"11:00"},{"time":"12:00","title":"Cierre"}]"""))).Props.Items;

        Assert.Equal(new[] { new TimelineHorizontalItem("12:00", "Cierre") }, items);
        Assert.Equal(2, Anotaciones());
    }
}
