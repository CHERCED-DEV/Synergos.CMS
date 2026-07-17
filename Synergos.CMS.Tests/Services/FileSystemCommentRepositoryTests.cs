using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemCommentRepository"/> cubriendo el
/// modelo anidado (ParentId) + reacciones (Likes) de Ola 230 (ADR 0100):
/// empty / happy / nested / like / backward-compat / idempotente.
/// </summary>
public sealed class FileSystemCommentRepositoryTests : IDisposable
{
    private const int NodeId = 1234;

    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;
    private readonly FileSystemCommentRepository _sut;

    public FileSystemCommentRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-comments-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
        // RequireModeration=false ⇒ comments aprobados al crear (visibles).
        var options = Options.Create(new CommentsSettings
        {
            StorageRoot = "App_Data/syn-comments/",
            RequireModeration = false,
            MaxBodyLengthChars = 2000,
        });
        _sut = new FileSystemCommentRepository(
            options,
            _env,
            NullLogger<FileSystemCommentRepository>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ── empty ────────────────────────────────────────────────────

    [Fact]
    public void GetApprovedForNode_EmptyStore_ReturnsEmpty()
    {
        var result = _sut.GetApprovedForNode(NodeId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LikeAsync_NonexistentComment_ReturnsNull()
    {
        var result = await _sut.LikeAsync(NodeId, "does-not-exist", CancellationToken.None);

        Assert.Null(result);
    }

    // ── happy: top-level ─────────────────────────────────────────

    [Fact]
    public async Task AddAsync_TopLevel_PersistsWithNullParentAndZeroLikes()
    {
        var comment = await _sut.AddAsync(
            new NewComment(NodeId, MemberKey: null, AuthorName: "Alice", Body: "Hola"),
            CancellationToken.None);

        Assert.Null(comment.ParentId);
        Assert.Equal(0, comment.Likes);
        Assert.True(comment.Approved);

        var approved = _sut.GetApprovedForNode(NodeId);
        Assert.Single(approved);
        Assert.Equal(comment.Id, approved[0].Id);
    }

    // ── nested (anidación de 1 nivel) ────────────────────────────

    [Fact]
    public async Task AddAsync_Reply_AnchorsToParent()
    {
        var parent = await _sut.AddAsync(
            new NewComment(NodeId, null, "Alice", "Pregunta"), CancellationToken.None);

        var reply = await _sut.AddAsync(
            new NewComment(NodeId, null, "Bob", "Respuesta",
                ParentId: Guid.ParseExact(parent.Id, "N")),
            CancellationToken.None);

        Assert.NotNull(reply.ParentId);
        Assert.Equal(parent.Id, reply.ParentId!.Value.ToString("N"));
    }

    [Fact]
    public async Task AddAsync_ReplyToReply_FlattensToGrandparentTopLevel()
    {
        var parent = await _sut.AddAsync(
            new NewComment(NodeId, null, "Alice", "Pregunta"), CancellationToken.None);
        var reply = await _sut.AddAsync(
            new NewComment(NodeId, null, "Bob", "Respuesta",
                ParentId: Guid.ParseExact(parent.Id, "N")),
            CancellationToken.None);

        // Responder a una respuesta debe re-anclar al top-level (2 niveles máx).
        var replyToReply = await _sut.AddAsync(
            new NewComment(NodeId, null, "Carol", "Sub-respuesta",
                ParentId: Guid.ParseExact(reply.Id, "N")),
            CancellationToken.None);

        Assert.Equal(parent.Id, replyToReply.ParentId!.Value.ToString("N"));
    }

    [Fact]
    public async Task AddAsync_ReplyToPhantomParent_DegradesToTopLevel()
    {
        var reply = await _sut.AddAsync(
            new NewComment(NodeId, null, "Bob", "Huérfano", ParentId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(reply.ParentId);
    }

    // ── like (reacción) ──────────────────────────────────────────

    [Fact]
    public async Task LikeAsync_ApprovedComment_IncrementsCount()
    {
        var comment = await _sut.AddAsync(
            new NewComment(NodeId, null, "Alice", "Hola"), CancellationToken.None);

        var first = await _sut.LikeAsync(NodeId, comment.Id, CancellationToken.None);
        var second = await _sut.LikeAsync(NodeId, comment.Id, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(1, first!.Likes);
        Assert.NotNull(second);
        Assert.Equal(2, second!.Likes);

        // Persistido: una recarga refleja el contador.
        var reloaded = _sut.GetApprovedForNode(NodeId).Single();
        Assert.Equal(2, reloaded.Likes);
    }

    // ── backward-compat: JSON legacy sin ParentId/Likes ──────────

    [Fact]
    public void GetApprovedForNode_LegacyJsonWithoutNewFields_DeserializesAsTopLevelZeroLikes()
    {
        // Simula un JSON persistido antes de Ola 230 (sin ParentId ni Likes).
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-comments");
        Directory.CreateDirectory(dir);
        var legacy = """
        [
          {
            "Id": "abc123",
            "NodeId": 1234,
            "MemberKey": null,
            "AuthorName": "Legacy",
            "Body": "Comentario viejo",
            "CreatedAtUtc": "2025-01-01T00:00:00Z",
            "Approved": true
          }
        ]
        """;
        File.WriteAllText(Path.Combine(dir, $"{NodeId}.json"), legacy);

        var result = _sut.GetApprovedForNode(NodeId);

        Assert.Single(result);
        Assert.Null(result[0].ParentId);
        Assert.Equal(0, result[0].Likes);
        Assert.Equal("Legacy", result[0].AuthorName);
    }

    // ── idempotente: like sobre comment no aprobado es no-op ──────

    [Fact]
    public async Task LikeAsync_PendingComment_ReturnsNullDoesNotMutate()
    {
        // Repo con moderación ⇒ comentario nuevo queda pendiente.
        var moderatedOptions = Options.Create(new CommentsSettings
        {
            StorageRoot = "App_Data/syn-comments/",
            RequireModeration = true,
        });
        var moderatedSut = new FileSystemCommentRepository(
            moderatedOptions, _env, NullLogger<FileSystemCommentRepository>.Instance);

        var pending = await moderatedSut.AddAsync(
            new NewComment(NodeId, null, "Alice", "Pendiente"), CancellationToken.None);
        Assert.False(pending.Approved);

        var result = await moderatedSut.LikeAsync(NodeId, pending.Id, CancellationToken.None);

        Assert.Null(result);
        var stillPending = moderatedSut.GetPendingForNode(NodeId).Single();
        Assert.Equal(0, stillPending.Likes);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) =>
            ContentRootPath = contentRootPath;

        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
