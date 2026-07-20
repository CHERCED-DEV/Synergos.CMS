using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Smoke tests del contrato NUEVO de <see cref="BlogsController"/> (OLA 6 Blogs,
/// doc 21 §2.3): DMs, notificaciones, explore/trending, long-form (article),
/// guardados y studio. Construye el controller con los stubs reales del motor
/// social (lógica pura, ADR 0002) — verifica el wiring/shape sin HTTP. No cubre
/// los endpoints de la OLA 3 (feed/post/react/follow/profile) ya existentes.
/// </summary>
public sealed class BlogsControllerOla6Tests
{
    // Construye el controller con stubs reales; siembra DMs + guardados como en boot.
    private static async Task<BlogsController> BuildAsync()
    {
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        var stream = new StubContentStream(graph, reactions);
        var profiles = new StubSocialProfileProjection();
        var comments = new FakeCommentRepository();
        var messaging = new StubMessagingService();
        var collections = new StubUserCollection();
        var notifications = new StubNotificationFeed(graph, reactions);

        await BlogsDemoSeeder.SeedAsync(messaging, collections);

        // El actor sale del GATE, no de un ?user= (T2-Blogs): se simula a "act-elena"
        // autenticada, que es de quien está sembrada la bandeja.
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(true);
        gate.CurrentMemberEmail.Returns("act-elena");

        return new BlogsController(
            stream, graph, reactions, profiles, comments, messaging, collections, notifications, gate);
    }

    private static T Body<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    [Fact] // DMs: la bandeja sembrada de elena trae hilos; el detalle del hilo trae mensajes
    public async Task Messages_And_Thread_ReturnSeededDms()
    {
        var c = await BuildAsync();

        var inbox = Body<BlogsController.MessagesResponse>(
            await c.Messages(CancellationToken.None));
        Assert.NotEmpty(inbox.Threads);

        var threadId = inbox.Threads[0].ThreadId;
        var detail = Body<BlogsController.ThreadResponse>(
            await c.Thread(threadId, CancellationToken.None));
        Assert.Equal(threadId, detail.Thread.ThreadId);
        Assert.NotEmpty(detail.Thread.Messages);
        Assert.Equal(2, detail.Thread.Participants.Count);
    }

    [Fact] // DM: enviar un mensaje nuevo abre/retoma el hilo y devuelve el detalle
    public async Task Message_Send_ReturnsThreadWithMessage()
    {
        var c = await BuildAsync();

        var res = await c.Message(
            new BlogsController.SendMessageRequest(From: "act-elena", To: "act-andres", Body: "Hola Andrés!"),
            CancellationToken.None);

        var thread = Body<BlogsController.ThreadResponse>(res).Thread;
        Assert.Contains(thread.Messages, m => m.Body == "Hola Andrés!");
    }

    [Fact] // DM inválido: sin destinatario → 400
    public async Task Message_MissingRecipient_BadRequest()
    {
        var c = await BuildAsync();

        var res = await c.Message(
            new BlogsController.SendMessageRequest(From: "act-elena", To: null, Body: "x"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact] // Notificaciones: elena tiene eventos derivados (follow/reacción)
    public async Task Notifications_ReturnDerivedEvents()
    {
        var c = await BuildAsync();

        var res = Body<BlogsController.NotificationsResponse>(
            await c.Notifications(CancellationToken.None));

        Assert.NotEmpty(res.Notifications);
        Assert.All(res.Notifications, n => Assert.False(string.IsNullOrWhiteSpace(n.Text)));
    }

    [Fact] // Explore: sin filtros trae posts + hashtags trending
    public async Task Explore_ReturnsPostsAndTrending()
    {
        var c = await BuildAsync();

        var res = Body<BlogsController.ExploreResponse>(
            await c.Explore(q: null, tag: null, CancellationToken.None));

        Assert.NotEmpty(res.Posts);
        // Los posts sembrados no traen #hashtags; trending puede venir vacío — el
        // contrato es que la propiedad existe (no null).
        Assert.NotNull(res.Trending);
    }

    [Fact] // Explore: filtro por texto reduce los resultados
    public async Task Explore_TextFilter_NarrowsResults()
    {
        var c = await BuildAsync();

        var res = Body<BlogsController.ExploreResponse>(
            await c.Explore(q: "arquitectura", tag: null, CancellationToken.None));

        Assert.All(res.Posts, p =>
            Assert.Contains("arquitectura", p.Body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // Long-form: crear artículo → item Kind=article; luego aparece en el feed
    public async Task Article_Create_IsArticleKindAndAppearsInFeed()
    {
        var c = await BuildAsync();

        var created = Body<BlogsController.CreatePostResponse>(
            await c.Article(
                new BlogsController.CreateArticleRequest(
                    Author: "act-elena",
                    Title: "Diseño de sistemas resilientes",
                    Body: "Un tratado sobre acoplamiento bajo.",
                    Tags: new[] { "arquitectura", "#sistemas" }),
                CancellationToken.None));

        Assert.Equal("article", created.Post.Kind);
        Assert.Contains("Diseño de sistemas resilientes", created.Post.Body);

        // El artículo también aparece en el feed (polimorfismo con el post).
        var feed = Body<BlogsController.FeedResponse>(
            await c.Feed(scope: null, cursor: null, actorId: null, kind: "article", CancellationToken.None));
        Assert.Contains(feed.Posts, p => p.Id == created.Post.Id);
    }

    [Fact] // Long-form inválido: sin título → 400
    public async Task Article_MissingTitle_BadRequest()
    {
        var c = await BuildAsync();

        var res = await c.Article(
            new BlogsController.CreateArticleRequest("act-elena", Title: null, Body: "cuerpo", Tags: null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact] // Guardados: la colección sembrada de elena trae posts; save/unsave togglea
    public async Task Saved_SeededThenToggle()
    {
        var c = await BuildAsync();

        var seeded = Body<BlogsController.FeedResponse>(
            await c.Saved(CancellationToken.None));
        Assert.NotEmpty(seeded.Posts);

        // Guardar post-002 (no estaba) y verificar que aparece.
        Body<BlogsController.SavedStateResponse>(
            await c.Save(new BlogsController.SaveRequest("act-elena", "post-002"), CancellationToken.None));
        var afterSave = Body<BlogsController.FeedResponse>(
            await c.Saved(CancellationToken.None));
        Assert.Contains(afterSave.Posts, p => p.Id == "post-002");

        // Quitarlo y verificar que desaparece.
        Body<BlogsController.SavedStateResponse>(
            await c.Unsave("post-002", CancellationToken.None));
        var afterRemove = Body<BlogsController.FeedResponse>(
            await c.Saved(CancellationToken.None));
        Assert.DoesNotContain(afterRemove.Posts, p => p.Id == "post-002");
    }

    [Fact] // Studio: métricas del creador (seguidores/alcance/engagement) + top posts
    public async Task Studio_ReturnsCreatorMetrics()
    {
        var c = await BuildAsync();

        var res = Body<BlogsController.StudioResponse>(
            await c.Studio(CancellationToken.None));

        Assert.True(res.Metrics.Followers >= 0);
        Assert.True(res.Metrics.Posts > 0);        // elena tiene posts sembrados
        Assert.True(res.Metrics.Reach >= 0);
        Assert.NotNull(res.TopPosts);
    }

    // ICommentRepository mínimo — el contrato de OLA 6 no toca comentarios, pero
    // el ctor del controller lo exige. Solo GetApprovedForNode se usa (post detail).
    private sealed class FakeCommentRepository : ICommentRepository
    {
        public IReadOnlyList<Comment> GetApprovedForNode(int nodeId) => Array.Empty<Comment>();
        public Task<Comment> AddAsync(NewComment comment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<Comment?> LikeAsync(int nodeId, string commentId, CancellationToken cancellationToken) =>
            Task.FromResult<Comment?>(null);
        public IReadOnlyList<Comment> GetPendingForNode(int nodeId) => Array.Empty<Comment>();
        public IReadOnlyList<Comment> GetAllPending(int limit) => Array.Empty<Comment>();
        public Task<bool> ApproveAsync(int nodeId, string commentId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> RejectAsync(int nodeId, string commentId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public PendingCommentsPage GetPendingPage(int page, int pageSize, int? nodeIdFilter = null) =>
            new(Array.Empty<Comment>(), page, pageSize, 0);
        public Task<int> BulkApproveAsync(IReadOnlyList<CommentRef> refs, CancellationToken cancellationToken) =>
            Task.FromResult(0);
        public Task<int> BulkRejectAsync(IReadOnlyList<CommentRef> refs, CancellationToken cancellationToken) =>
            Task.FromResult(0);
        public IReadOnlyList<Comment> ReadByRefs(IReadOnlyList<CommentRef> refs) => Array.Empty<Comment>();
        public Task<int> RestoreAsync(IReadOnlyList<Comment> items, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
