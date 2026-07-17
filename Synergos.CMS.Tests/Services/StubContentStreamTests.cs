using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubContentStream"/> (seam <see cref="IContentStream"/> —
/// abstracción reusable de feed/contenido, dominio Blogs): los 4 casos canónicos
/// (ADR 0075) — empty / happy / filter / idempotent — más el detalle, la creación
/// (optimistic insert) y el polimorfismo por <c>Kind</c> (reuso de Educación).
/// </summary>
public class StubContentStreamTests
{
    private static StubContentStream Make()
    {
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        // Reloj determinista para que los items creados ordenen estable.
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(graph, reactions, () => clock);
    }

    [Fact] // empty: feed "Siguiendo" de un actor que no sigue a nadie → vacío
    public async Task Feed_FollowingUnknownActor_ReturnsEmpty()
    {
        var page = await Make().GetFeedAsync(
            new FeedQuery(Scope: FeedScope.Following, AuthorId: "act-nadie"));

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact] // happy: "Para ti" devuelve el feed sembrado, más reciente primero
    public async Task Feed_ForYou_ReturnsSeededNewestFirst()
    {
        var page = await Make().GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou));

        Assert.NotEmpty(page.Items);
        // Orden descendente por fecha.
        var dates = page.Items.Select(i => i.CreatedUtc).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);
        // Cada item trae autor resuelto + métricas.
        Assert.All(page.Items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Author.Handle));
            Assert.NotNull(i.Metrics);
        });
    }

    [Fact] // filter: "Siguiendo" solo trae items de los autores que el actor sigue
    public async Task Feed_Following_FiltersToFollowees()
    {
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        var stream = new StubContentStream(graph, reactions);

        var followees = new HashSet<string>(await graph.GetFollowingAsync("act-elena"));
        Assert.NotEmpty(followees); // sanity: la semilla pone a elena siguiendo a alguien

        var page = await stream.GetFeedAsync(
            new FeedQuery(Scope: FeedScope.Following, AuthorId: "act-elena"));

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, i => Assert.Contains(i.Author.Id, followees));
    }

    [Fact] // filter (polimorfismo): por Kind=article solo trae artículos — reuso Educación
    public async Task Feed_ByKind_FiltersToKind()
    {
        var page = await Make().GetFeedAsync(new FeedQuery(Kind: "article"));

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, i => Assert.Equal("article", i.Kind));
    }

    [Fact] // filter: Author scope solo trae los posts de ese autor
    public async Task Feed_AuthorScope_FiltersToAuthor()
    {
        var page = await Make().GetFeedAsync(
            new FeedQuery(Scope: FeedScope.Author, AuthorId: "act-sofia"));

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, i => Assert.Equal("act-sofia", i.Author.Id));
    }

    [Fact] // paginación: el cursor devuelve la siguiente página sin solapar
    public async Task Feed_Cursor_PaginatesWithoutOverlap()
    {
        var stream = Make();
        var first = await stream.GetFeedAsync(new FeedQuery(PageSize: 3));
        Assert.Equal(3, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await stream.GetFeedAsync(new FeedQuery(Cursor: first.NextCursor, PageSize: 3));
        var firstIds = first.Items.Select(i => i.Id).ToHashSet();
        Assert.All(second.Items, i => Assert.DoesNotContain(i.Id, firstIds));
    }

    [Fact] // idempotent: misma query → mismo resultado (feed determinista)
    public async Task Feed_SameQuery_IsDeterministic()
    {
        var stream = Make();
        var a = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou));
        var b = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou));

        Assert.Equal(a.Items.Select(i => i.Id), b.Items.Select(i => i.Id));
        Assert.Equal(a.NextCursor, b.NextCursor);
    }

    [Fact] // detail happy: GetItem resuelve un post sembrado
    public async Task GetItem_Seeded_ReturnsItem()
    {
        var item = await Make().GetItemAsync("post-001");

        Assert.NotNull(item);
        Assert.Equal("post-001", item!.Id);
        Assert.False(string.IsNullOrWhiteSpace(item.Body));
    }

    [Fact] // detail empty: id inexistente → null
    public async Task GetItem_Unknown_ReturnsNull()
    {
        Assert.Null(await Make().GetItemAsync("no-existe"));
    }

    [Fact] // create happy: publicar inserta el item al top del feed
    public async Task Create_InsertsAtTopOfFeed()
    {
        var stream = Make();
        var created = await stream.CreateAsync(new NewContentItem("act-elena", "Mi primer post de prueba"));

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("post", created.Kind);

        var page = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou));
        Assert.Equal(created.Id, page.Items[0].Id); // reloj fijo > Epoch ⇒ va al top
    }

    [Fact] // create con Kind=lesson (Educación) preserva el polimorfismo
    public async Task Create_WithLessonKind_IsRetrievableByKind()
    {
        var stream = Make();
        var created = await stream.CreateAsync(
            new NewContentItem("act-sofia", "Lección 1: introducción", Kind: "lesson"));

        var page = await stream.GetFeedAsync(new FeedQuery(Kind: "lesson"));
        Assert.Contains(page.Items, i => i.Id == created.Id);
    }

    [Fact] // create empty: sin cuerpo ni media lanza
    public async Task Create_EmptyBodyAndMedia_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().CreateAsync(new NewContentItem("act-elena", "", null)));
    }

    [Fact] // métricas: las reacciones del feed se proyectan en vivo desde IReactionService
    public async Task Feed_ReactionMetrics_ReflectReactionService()
    {
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        var stream = new StubContentStream(graph, reactions);

        // El seed pone 3 reacciones en post-001.
        var item = await stream.GetItemAsync("post-001");
        Assert.NotNull(item);
        Assert.Equal(3, item!.Metrics.Reactions);

        // Tras retirar una, el feed lo refleja.
        await reactions.UnreactAsync("act-mateo", "post-001");
        var after = await stream.GetItemAsync("post-001");
        Assert.Equal(2, after!.Metrics.Reactions);
    }
}
