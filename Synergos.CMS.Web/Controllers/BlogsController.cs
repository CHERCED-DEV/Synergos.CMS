using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON de la red social (dominio Blogs — OLA 3). Es el equivalente social
/// del <see cref="ShopCatalogController"/>/<see cref="BookingController"/>: delega
/// el feed/contenido a <see cref="IContentStream"/> (abstracción REUSABLE — la
/// reusa Educación por polimorfismo), el grafo follow a
/// <see cref="ISocialGraphService"/>, las reacciones a <see cref="IReactionService"/>,
/// el perfil a <see cref="ISocialProfileProjection"/>, y los comentarios del post
/// al <see cref="ICommentRepository"/> EXISTENTE (no se crea otro — ya hace hilos
/// anidados + likes + moderación). Expone el contrato que el módulo Angular
/// <c>&lt;synergos-blogs&gt;</c> consume.
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en
/// los seams (Application, sin Umbraco — ADR 0002). Los seams se cambian por
/// adapters reales (índice/store de actividad, store de grafo, etc.) sin tocar
/// este controller. API pública en MVP (sin auth-gate): el visitante lee el feed
/// sin login; las acciones de escritura (publicar/reaccionar/seguir) se atan a
/// Members (PS3) en una iteración — el contrato no cambia.
///
/// <para><b>Mapeo post-id ↔ nodeId de comentarios.</b> Los posts tienen id string
/// (<c>post-001</c>); <see cref="ICommentRepository"/> indexa por <c>int nodeId</c>.
/// Se deriva un nodeId estable y determinista del id del post (hash FNV-1a) para
/// reusar el store de comentarios sin schema nuevo — la misma técnica que usaría
/// cualquier objeto comentable que no sea un nodo Umbraco.</para>
/// </remarks>
[ApiController]
[Route("api/blogs")]
public sealed class BlogsController : ControllerBase
{
    private readonly IContentStream _stream;
    private readonly ISocialGraphService _graph;
    private readonly IReactionService _reactions;
    private readonly ISocialProfileProjection _profiles;
    private readonly ICommentRepository _comments;

    public BlogsController(
        IContentStream stream,
        ISocialGraphService graph,
        IReactionService reactions,
        ISocialProfileProjection profiles,
        ICommentRepository comments)
    {
        _stream = stream;
        _graph = graph;
        _reactions = reactions;
        _profiles = profiles;
        _comments = comments;
    }

    // ── 1. Feed ────────────────────────────────────────────────────
    // GET /api/blogs/feed?scope=foryou|following&cursor=  → { posts:[...], nextCursor }
    [HttpGet("feed")]
    public async Task<IActionResult> Feed(
        [FromQuery] string? scope,
        [FromQuery] string? cursor,
        [FromQuery] string? actorId,
        [FromQuery] string? kind,
        CancellationToken cancellationToken)
    {
        var feedScope = ParseScope(scope);

        // "Siguiendo" sin actor de contexto → usa el actor demo "yo" para que la
        // demo muestre un feed poblado (en prod sale del Member autenticado).
        var contextActor = string.IsNullOrWhiteSpace(actorId)
            ? (feedScope == FeedScope.Following ? DemoCurrentActor : null)
            : actorId.Trim();

        var page = await _stream.GetFeedAsync(
            new FeedQuery(Scope: feedScope, Cursor: cursor, AuthorId: contextActor, Kind: kind),
            cancellationToken);

        var posts = new List<PostDto>(page.Items.Count);
        foreach (var item in page.Items)
        {
            posts.Add(await ToPostDto(item, contextActor, cancellationToken));
        }

        return Ok(new FeedResponse(Posts: posts, NextCursor: page.NextCursor));
    }

    // ── 2. Post detail + comments ──────────────────────────────────
    // GET /api/blogs/post/{id} → { post, comments:[...] }
    [HttpGet("post/{id}")]
    public async Task<IActionResult> Post(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del post es requerido." });
        }

        var item = await _stream.GetItemAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new { error = $"Post '{id}' no encontrado." });
        }

        var post = await ToPostDto(item, DemoCurrentActor, cancellationToken);
        var comments = _comments.GetApprovedForNode(NodeIdFor(id))
            .Select(ToCommentDto)
            .ToList();

        return Ok(new PostDetailResponse(Post: post, Comments: comments));
    }

    // ── 3. Create post ─────────────────────────────────────────────
    // POST /api/blogs/post { body, mediaUrl? } → { post }
    [HttpPost("post")]
    public async Task<IActionResult> Create([FromBody] CreatePostRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.MediaUrl)))
        {
            return BadRequest(new { error = "El post requiere cuerpo o media." });
        }

        var authorId = string.IsNullOrWhiteSpace(request.AuthorId) ? DemoCurrentActor : request.AuthorId.Trim();

        ContentStreamItem created;
        try
        {
            created = await _stream.CreateAsync(
                new NewContentItem(
                    AuthorId: authorId,
                    Body: request.Body ?? string.Empty,
                    MediaUrl: request.MediaUrl,
                    Kind: string.IsNullOrWhiteSpace(request.Kind) ? "post" : request.Kind.Trim()),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var post = await ToPostDto(created, authorId, cancellationToken);
        return Ok(new CreatePostResponse(Post: post));
    }

    // ── 4. React ───────────────────────────────────────────────────
    // POST /api/blogs/post/{id}/react { type } → { reactions }
    [HttpPost("post/{id}/react")]
    public async Task<IActionResult> React(
        string id,
        [FromBody] ReactRequest? request,
        [FromQuery] string? actorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del post es requerido." });
        }

        var actor = string.IsNullOrWhiteSpace(actorId) ? DemoCurrentActor : actorId.Trim();
        var type = string.IsNullOrWhiteSpace(request?.Type) ? "like" : request!.Type.Trim();

        ReactionState state;
        try
        {
            state = await _reactions.ReactAsync(actor, id, type, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ReactResponse(Reactions: ToReactionsDto(state)));
    }

    // ── 5. Follow ──────────────────────────────────────────────────
    // POST /api/blogs/follow/{authorId} → { following }
    [HttpPost("follow/{authorId}")]
    public async Task<IActionResult> Follow(
        string authorId,
        [FromQuery] string? followerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorId))
        {
            return BadRequest(new { error = "El authorId es requerido." });
        }

        var follower = string.IsNullOrWhiteSpace(followerId) ? DemoCurrentActor : followerId.Trim();

        // Toggle: si ya sigue → unfollow; si no → follow. Idempotente en ambas vías.
        var alreadyFollowing = await _graph.IsFollowingAsync(follower, authorId, cancellationToken);
        var state = alreadyFollowing
            ? await _graph.UnfollowAsync(follower, authorId, cancellationToken)
            : await _graph.FollowAsync(follower, authorId, cancellationToken);

        return Ok(new FollowResponse(
            Following: state.Following,
            FollowerId: state.FollowerId,
            AuthorId: state.FolloweeId,
            Followers: state.FolloweeFollowers,
            FollowingCount: state.FolloweeFollowing));
    }

    // ── 6. Profile ─────────────────────────────────────────────────
    // GET /api/blogs/profile/{handle} → { author, posts:[...], stats }
    [HttpGet("profile/{handle}")]
    public async Task<IActionResult> Profile(string handle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return BadRequest(new { error = "El handle es requerido." });
        }

        var profile = await _profiles.GetByHandleAsync(handle, cancellationToken);
        if (profile is null)
        {
            return NotFound(new { error = $"Perfil '{handle}' no encontrado." });
        }

        var page = await _stream.GetFeedAsync(
            new FeedQuery(Scope: FeedScope.Author, AuthorId: profile.ActorId),
            cancellationToken);

        var posts = new List<PostDto>(page.Items.Count);
        foreach (var item in page.Items)
        {
            posts.Add(await ToPostDto(item, DemoCurrentActor, cancellationToken));
        }

        var counts = await _graph.GetCountsAsync(profile.ActorId, cancellationToken);

        return Ok(new ProfileResponse(
            Author: ToProfileDto(profile),
            Posts: posts,
            Stats: new ProfileStatsDto(
                Posts: posts.Count,
                Followers: counts.Followers,
                Following: counts.Following)));
    }

    // ── Helpers ────────────────────────────────────────────────────

    // Actor "yo" de la demo (en prod = Member autenticado). Permite que las
    // acciones de escritura y el feed "Siguiendo" rindan sin login en la demo.
    private const string DemoCurrentActor = "act-elena";

    private async Task<PostDto> ToPostDto(ContentStreamItem item, string? viewerId, CancellationToken cancellationToken)
    {
        var reactions = await _reactions.GetStateAsync(item.Id, viewerId, cancellationToken);
        return new PostDto(
            Id: item.Id,
            Kind: item.Kind,
            Author: new AuthorDto(
                Id: item.Author.Id,
                Handle: item.Author.Handle,
                DisplayName: item.Author.DisplayName,
                AvatarUrl: item.Author.AvatarUrl,
                Verified: item.Author.Verified),
            Body: item.Body,
            MediaUrl: item.MediaUrl,
            CreatedUtc: item.CreatedUtc,
            Reactions: ToReactionsDto(reactions),
            Comments: item.Metrics.Comments,
            Reposts: item.Metrics.Reposts);
    }

    private static ReactionsDto ToReactionsDto(ReactionState state) => new(
        Total: state.Total,
        CountsByType: state.CountsByType,
        MyReaction: state.MyReaction);

    private static CommentDto ToCommentDto(Comment c) => new(
        Id: c.Id,
        Author: c.AuthorName,
        Body: c.Body,
        CreatedUtc: c.CreatedAtUtc,
        ParentId: c.ParentId?.ToString(),
        Likes: c.Likes);

    private static AuthorDto ToProfileDto(SocialProfile p) => new(
        Id: p.ActorId,
        Handle: p.Handle,
        DisplayName: p.DisplayName,
        AvatarUrl: p.AvatarUrl,
        Verified: p.Verified);

    private static FeedScope ParseScope(string? scope) => (scope?.Trim().ToLowerInvariant()) switch
    {
        "following" => FeedScope.Following,
        "author" => FeedScope.Author,
        _ => FeedScope.ForYou,
    };

    // nodeId determinista a partir del id string del post (FNV-1a 32-bit, forzado
    // positivo) para reusar el ICommentRepository (indexado por int) sin schema nuevo.
    private static int NodeIdFor(string postId)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffset;
        foreach (var ch in postId)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }
        return (int)(hash & 0x7FFFFFFF);
    }

    // ── Request DTOs (binding del módulo blogs) ────────────────────

    /// <summary>POST /api/blogs/post — cuerpo + media opcional (+ autor/kind opcionales).</summary>
    public sealed record CreatePostRequest(string? Body, string? MediaUrl, string? AuthorId, string? Kind);

    /// <summary>POST /api/blogs/post/{id}/react — el tipo de reacción.</summary>
    public sealed record ReactRequest(string? Type);

    // ── Response DTOs (JSON estable para la UI) ────────────────────

    public sealed record FeedResponse(IReadOnlyList<PostDto> Posts, string? NextCursor);

    public sealed record PostDetailResponse(PostDto Post, IReadOnlyList<CommentDto> Comments);

    public sealed record CreatePostResponse(PostDto Post);

    public sealed record ReactResponse(ReactionsDto Reactions);

    public sealed record FollowResponse(
        bool Following,
        string FollowerId,
        string AuthorId,
        int Followers,
        int FollowingCount);

    public sealed record ProfileResponse(AuthorDto Author, IReadOnlyList<PostDto> Posts, ProfileStatsDto Stats);

    public sealed record PostDto(
        string Id,
        string Kind,
        AuthorDto Author,
        string Body,
        string? MediaUrl,
        DateTime CreatedUtc,
        ReactionsDto Reactions,
        int Comments,
        int Reposts);

    public sealed record AuthorDto(
        string Id,
        string Handle,
        string DisplayName,
        string? AvatarUrl,
        bool Verified);

    public sealed record ReactionsDto(
        int Total,
        IReadOnlyDictionary<string, int> CountsByType,
        string? MyReaction);

    public sealed record CommentDto(
        string Id,
        string Author,
        string Body,
        DateTime CreatedUtc,
        string? ParentId,
        int Likes);

    public sealed record ProfileStatsDto(int Posts, int Followers, int Following);
}
