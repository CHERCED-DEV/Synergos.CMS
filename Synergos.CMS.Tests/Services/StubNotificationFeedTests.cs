using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubNotificationFeed"/> (seam <see cref="INotificationFeed"/>
/// — centro de notificaciones, dominio Blogs OLA 6): los 4 casos canónicos (ADR
/// 0075) — empty / happy / filter / idempotent — más la derivación de eventos
/// desde el grafo + reacciones vivos (sin store paralelo).
/// </summary>
public class StubNotificationFeedTests
{
    private static readonly DateTime Clock = new(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);

    private static (StubNotificationFeed Feed, StubSocialGraphService Graph, StubReactionService Reactions) Make()
    {
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        var feed = new StubNotificationFeed(graph, reactions, () => Clock);
        return (feed, graph, reactions);
    }

    [Fact] // empty: destinatario desconocido (o vacío) → sin notificaciones, no lanza
    public async Task GetFor_UnknownOrBlank_ReturnsEmpty()
    {
        var (feed, _, _) = Make();

        Assert.Empty(await feed.GetForAsync("act-nadie"));
        Assert.Empty(await feed.GetForAsync(""));
        Assert.Empty(await feed.GetForAsync("   "));
    }

    [Fact] // happy: elena tiene seguidores + reacciones sembradas en sus posts → eventos
    public async Task GetFor_SeededRecipient_DerivesFollowAndReactionEvents()
    {
        var (feed, _, _) = Make();

        var events = await feed.GetForAsync("act-elena");

        Assert.NotEmpty(events);
        // Hay al menos un follow (alguien la sigue) y una reacción (a post-001/005).
        Assert.Contains(events, e => e.Type == "follow");
        Assert.Contains(events, e => e.Type == "reaction" && e.ObjectId == "post-001");
        // Orden descendente por fecha.
        var dates = events.Select(e => e.CreatedUtc).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);
        // Cada evento trae actor resuelto + texto.
        Assert.All(events, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Actor.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(e.Text));
        });
    }

    [Fact] // filter: un follow NUEVO al destinatario aparece derivado del grafo vivo
    public async Task GetFor_ReflectsLiveGraph_NewFollowerShowsUp()
    {
        var (feed, graph, _) = Make();

        // andres empieza a seguir a mateo (no lo seguía en la semilla).
        await graph.FollowAsync("act-andres", "act-mateo");

        var events = await feed.GetForAsync("act-mateo");

        Assert.Contains(events, e => e.Type == "follow" && e.Actor.Id == "act-andres");
    }

    [Fact] // idempotent: dos consultas seguidas sin cambios → mismo resultado (ids estables)
    public async Task GetFor_Twice_IsStable()
    {
        var (feed, _, _) = Make();

        var first = await feed.GetForAsync("act-elena");
        var second = await feed.GetForAsync("act-elena");

        Assert.Equal(
            first.Select(e => e.Id).ToList(),
            second.Select(e => e.Id).ToList());
    }

    [Fact] // no se notifica la reacción propia del destinatario a su propio post
    public async Task GetFor_ExcludesSelfReactions()
    {
        var (feed, _, reactions) = Make();

        // elena reacciona a su propio post-005.
        await reactions.ReactAsync("act-elena", "post-005", "like");

        var events = await feed.GetForAsync("act-elena");

        Assert.DoesNotContain(events, e =>
            e.Type == "reaction" && e.ObjectId == "post-005" && e.Actor.Id == "act-elena");
    }

    [Fact] // limit capa la cantidad de eventos devueltos
    public async Task GetFor_RespectsLimit()
    {
        var (feed, _, _) = Make();

        var capped = await feed.GetForAsync("act-elena", limit: 1);

        Assert.True(capped.Count <= 1);
    }
}
