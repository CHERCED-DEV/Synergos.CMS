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
    private readonly IMessagingService _messaging;
    private readonly IUserCollection _collections;
    private readonly INotificationFeed _notifications;

    public BlogsController(
        IContentStream stream,
        ISocialGraphService graph,
        IReactionService reactions,
        ISocialProfileProjection profiles,
        ICommentRepository comments,
        IMessagingService messaging,
        IUserCollection collections,
        INotificationFeed notifications)
    {
        _stream = stream;
        _graph = graph;
        _reactions = reactions;
        _profiles = profiles;
        _comments = comments;
        _messaging = messaging;
        _collections = collections;
        _notifications = notifications;
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

    // ── 7. Long-form (artículos) ───────────────────────────────────
    // POST /api/blogs/article { author, title, body, tags } → { post }
    // Un artículo es un ContentStreamItem con Kind=article (polimorfismo con el
    // post): aparece en el feed y en el perfil como cualquier otro item. El
    // título + los tags se anteponen al cuerpo (el stream no tiene campos propios
    // para ellos — v1 simple; el adapter real los persiste como metadata).
    [HttpPost("article")]
    public async Task<IActionResult> Article([FromBody] CreateArticleRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "El artículo requiere título y cuerpo." });
        }

        var authorId = string.IsNullOrWhiteSpace(request.Author) ? DemoCurrentActor : request.Author.Trim();
        var body = ComposeArticleBody(request.Title.Trim(), request.Body.Trim(), request.Tags);

        ContentStreamItem created;
        try
        {
            created = await _stream.CreateAsync(
                new NewContentItem(AuthorId: authorId, Body: body, MediaUrl: null, Kind: "article"),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var post = await ToPostDto(created, authorId, cancellationToken);
        return Ok(new CreatePostResponse(Post: post));
    }

    // ── 8. DMs (mensajería directa — SH-7 v2) ──────────────────────
    // GET /api/blogs/messages?user= → inbox { threads:[...] }
    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] string? user, CancellationToken cancellationToken)
    {
        var who = string.IsNullOrWhiteSpace(user) ? DemoCurrentActor : user.Trim();
        var inbox = await _messaging.GetInboxAsync(who, cancellationToken);

        var threads = new List<DmThreadSummaryDto>(inbox.Count);
        foreach (var t in inbox)
        {
            threads.Add(new DmThreadSummaryDto(
                ThreadId: t.ThreadId,
                Participants: await ToParticipants(t.Participants, cancellationToken),
                LastMessagePreview: t.LastMessagePreview,
                LastMessageAt: t.LastMessageAt,
                MessageCount: t.MessageCount));
        }

        return Ok(new MessagesResponse(Threads: threads));
    }

    // GET /api/blogs/thread/{id} → { thread }
    [HttpGet("thread/{id}")]
    public async Task<IActionResult> Thread(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del hilo es requerido." });
        }

        var thread = await _messaging.GetThreadAsync(id, cancellationToken);
        if (thread is null)
        {
            return NotFound(new { error = $"Hilo '{id}' no encontrado." });
        }

        return Ok(new ThreadResponse(Thread: await ToDmThreadDto(thread, cancellationToken)));
    }

    // POST /api/blogs/message { from, to, body } → { thread }
    // Reusa el IMessagingService genérico con contexto "dm". Idempotente por
    // (contexto + par): re-abrir la misma conversación agrega el mensaje al hilo
    // existente en vez de duplicarlo.
    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] SendMessageRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "El mensaje requiere destinatario y cuerpo." });
        }

        var from = string.IsNullOrWhiteSpace(request.From) ? DemoCurrentActor : request.From.Trim();

        MessageThread thread;
        try
        {
            thread = await _messaging.StartThreadAsync(
                DmContext, from, request.To.Trim(), request.Body, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ThreadResponse(Thread: await ToDmThreadDto(thread, cancellationToken)));
    }

    // ── 9. Notificaciones ──────────────────────────────────────────
    // GET /api/blogs/notifications?user= → { notifications:[...] }
    // Eventos dirigidos (follow/reacción/mención) derivados del grafo + reacciones.
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] string? user, CancellationToken cancellationToken)
    {
        var who = string.IsNullOrWhiteSpace(user) ? DemoCurrentActor : user.Trim();
        var events = await _notifications.GetForAsync(who, cancellationToken: cancellationToken);

        var dtos = events.Select(n => new NotificationDto(
            Id: n.Id,
            Type: n.Type,
            Actor: new AuthorDto(n.Actor.Id, n.Actor.Handle, n.Actor.DisplayName, n.Actor.AvatarUrl, n.Actor.Verified),
            ObjectId: n.ObjectId,
            Text: n.Text,
            CreatedUtc: n.CreatedUtc)).ToList();

        return Ok(new NotificationsResponse(Notifications: dtos));
    }

    // ── 10. Explore / trending ─────────────────────────────────────
    // GET /api/blogs/explore?q=&tag= → { posts:[...], trending:[...] }
    // Posts que matchean el texto/hashtag + hashtags trending derivados del stream
    // (frecuencia de #tag ponderada por las reacciones de cada post).
    [HttpGet("explore")]
    public async Task<IActionResult> Explore(
        [FromQuery] string? q,
        [FromQuery] string? tag,
        CancellationToken cancellationToken)
    {
        var page = await _stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, PageSize: 100), cancellationToken);

        var trending = ComputeTrending(page.Items);

        IEnumerable<ContentStreamItem> matches = page.Items;
        var query = q?.Trim();
        var hashtag = NormalizeTag(tag);
        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches.Where(i => i.Body.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(hashtag))
        {
            matches = matches.Where(i => ExtractTags(i.Body).Contains(hashtag, StringComparer.OrdinalIgnoreCase));
        }

        var posts = new List<PostDto>();
        foreach (var item in matches)
        {
            posts.Add(await ToPostDto(item, DemoCurrentActor, cancellationToken));
        }

        return Ok(new ExploreResponse(Posts: posts, Trending: trending));
    }

    // ── 11. Guardados ──────────────────────────────────────────────
    // GET /api/blogs/saved?user= → { posts:[...] }
    [HttpGet("saved")]
    public async Task<IActionResult> Saved([FromQuery] string? user, CancellationToken cancellationToken)
    {
        var who = string.IsNullOrWhiteSpace(user) ? DemoCurrentActor : user.Trim();
        var items = await _collections.GetAsync(who, SavedCollection, cancellationToken);

        var posts = new List<PostDto>();
        foreach (var it in items)
        {
            var post = await _stream.GetItemAsync(it.ItemRef, cancellationToken);
            if (post is not null)
            {
                posts.Add(await ToPostDto(post, who, cancellationToken));
            }
        }

        return Ok(new FeedResponse(Posts: posts, NextCursor: null));
    }

    // POST /api/blogs/saved { user?, postId } → { saved:true }
    [HttpPost("saved")]
    public async Task<IActionResult> Save([FromBody] SaveRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PostId))
        {
            return BadRequest(new { error = "El postId es requerido." });
        }

        var who = string.IsNullOrWhiteSpace(request.User) ? DemoCurrentActor : request.User.Trim();
        await _collections.AddAsync(who, SavedCollection, request.PostId.Trim(), cancellationToken);
        return Ok(new SavedStateResponse(Saved: true, PostId: request.PostId.Trim()));
    }

    // DELETE /api/blogs/saved?user=&postId= → { saved:false }
    [HttpDelete("saved")]
    public async Task<IActionResult> Unsave(
        [FromQuery] string? user,
        [FromQuery] string? postId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postId))
        {
            return BadRequest(new { error = "El postId es requerido." });
        }

        var who = string.IsNullOrWhiteSpace(user) ? DemoCurrentActor : user.Trim();
        await _collections.RemoveAsync(who, SavedCollection, postId.Trim(), cancellationToken);
        return Ok(new SavedStateResponse(Saved: false, PostId: postId.Trim()));
    }

    // ── 12. Creator Studio ─────────────────────────────────────────
    // GET /api/blogs/studio?author= → { author, metrics }
    // Métricas del creador: seguidores (grafo), alcance (reacciones+comentarios
    // acumulados de sus posts) y engagement (alcance / posts).
    [HttpGet("studio")]
    public async Task<IActionResult> Studio([FromQuery] string? author, CancellationToken cancellationToken)
    {
        var authorId = string.IsNullOrWhiteSpace(author) ? DemoCurrentActor : author.Trim();
        var profile = await _profiles.GetByActorIdAsync(authorId, cancellationToken);

        var page = await _stream.GetFeedAsync(
            new FeedQuery(Scope: FeedScope.Author, AuthorId: authorId, PageSize: 100),
            cancellationToken);

        var counts = await _graph.GetCountsAsync(authorId, cancellationToken);

        var totalReactions = 0;
        var totalComments = 0;
        var topPosts = new List<StudioPostDto>();
        foreach (var item in page.Items)
        {
            var state = await _reactions.GetStateAsync(item.Id, null, cancellationToken);
            totalReactions += state.Total;
            totalComments += item.Metrics.Comments;
            topPosts.Add(new StudioPostDto(
                Id: item.Id,
                Kind: item.Kind,
                Excerpt: Excerpt(item.Body),
                Reactions: state.Total,
                Comments: item.Metrics.Comments));
        }

        var postCount = page.Items.Count;
        var reach = totalReactions + totalComments;
        var engagement = postCount > 0 ? Math.Round((double)reach / postCount, 2) : 0d;

        var top = topPosts
            .OrderByDescending(p => p.Reactions + p.Comments)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        return Ok(new StudioResponse(
            Author: profile is not null ? ToProfileDto(profile) : SyntheticAuthor(authorId),
            Metrics: new StudioMetricsDto(
                Followers: counts.Followers,
                Following: counts.Following,
                Posts: postCount,
                Reach: reach,
                Engagement: engagement),
            TopPosts: top));
    }

    // ── Helpers ────────────────────────────────────────────────────

    // Actor "yo" de la demo (en prod = Member autenticado). Permite que las
    // acciones de escritura y el feed "Siguiendo" rindan sin login en la demo.
    private const string DemoCurrentActor = "act-elena";

    // Contexto opaco de los hilos de DM en el IMessagingService genérico.
    private const string DmContext = "dm";

    // Colección de guardados en el IUserCollection genérico.
    private const string SavedCollection = "saved";

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
            Media: string.IsNullOrWhiteSpace(item.MediaUrl)
                ? System.Array.Empty<PostMediaDto>()
                : new[] { new PostMediaDto(item.MediaUrl!, "image", BuildMediaAlt(item.Body)) },
            CreatedUtc: item.CreatedUtc,
            Reactions: ToReactionsDto(reactions),
            Comments: item.Metrics.Comments,
            Reposts: item.Metrics.Reposts);
    }

    private static string BuildMediaAlt(string? body)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0) return "Imagen del post";
        return text.Length <= 80 ? text : text[..80].TrimEnd() + "…";
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

    // Antepone título + tags al cuerpo del artículo (el stream v1 no tiene campos
    // propios para ellos; el adapter real los persiste como metadata estructurada).
    private static string ComposeArticleBody(string title, string body, IReadOnlyList<string>? tags)
    {
        var normalizedTags = (tags ?? Array.Empty<string>())
            .Select(NormalizeTag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => "#" + t)
            .ToList();

        var tagLine = normalizedTags.Count > 0 ? "\n\n" + string.Join(" ", normalizedTags) : string.Empty;
        return $"# {title}\n\n{body}{tagLine}";
    }

    // Enriquece los ids de participantes de un hilo a autores (avatar + nombre).
    private async Task<IReadOnlyList<AuthorDto>> ToParticipants(
        IReadOnlyList<string> participantIds, CancellationToken cancellationToken)
    {
        var list = new List<AuthorDto>(participantIds.Count);
        foreach (var id in participantIds)
        {
            var profile = await _profiles.GetByActorIdAsync(id, cancellationToken);
            list.Add(profile is not null ? ToProfileDto(profile) : SyntheticAuthor(id));
        }
        return list;
    }

    private async Task<DmThreadDto> ToDmThreadDto(MessageThread thread, CancellationToken cancellationToken) => new(
        ThreadId: thread.ThreadId,
        Participants: await ToParticipants(thread.Participants, cancellationToken),
        Messages: thread.Messages.Select(m => new DmMessageDto(
            Id: m.MessageId,
            From: m.From,
            Body: m.Body,
            SentAt: m.SentAt)).ToList(),
        LastMessageAt: thread.LastMessageAt);

    // Hashtags trending: frecuencia de cada #tag en el feed, ponderada por las
    // reacciones del post que lo contiene (un tag en un post muy reaccionado sube).
    private IReadOnlyList<TrendingTagDto> ComputeTrending(IReadOnlyList<ContentStreamItem> items)
    {
        var scores = new Dictionary<string, (int Posts, int Weight)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var weight = 1 + _reactions.GetStateAsync(item.Id).GetAwaiter().GetResult().Total;
            foreach (var tag in ExtractTags(item.Body).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var current = scores.TryGetValue(tag, out var s) ? s : (Posts: 0, Weight: 0);
                scores[tag] = (current.Posts + 1, current.Weight + weight);
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value.Weight)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(kv => new TrendingTagDto(Tag: kv.Key, Posts: kv.Value.Posts, Score: kv.Value.Weight))
            .ToList();
    }

    // Extrae los hashtags (#tag) del cuerpo, sin el prefijo #, en minúsculas.
    private static IReadOnlyList<string> ExtractTags(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        var tags = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(body, @"#(\w[\w-]*)");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            tags.Add(m.Groups[1].Value.ToLowerInvariant());
        }
        return tags;
    }

    // Normaliza un tag de entrada: sin #, trim, minúsculas.
    private static string NormalizeTag(string? tag)
        => string.IsNullOrWhiteSpace(tag) ? string.Empty : tag.Trim().TrimStart('#').ToLowerInvariant();

    private static string Excerpt(string body)
    {
        var text = (body ?? string.Empty).Replace("\n", " ").Trim();
        return text.Length <= 120 ? text : text[..117] + "…";
    }

    // Autor sintético estable cuando el actor no está en el catálogo de perfiles.
    private static AuthorDto SyntheticAuthor(string actorId)
        => new(Id: actorId, Handle: actorId, DisplayName: actorId, AvatarUrl: null, Verified: false);

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

    /// <summary>POST /api/blogs/article — artículo long-form (Kind=article).</summary>
    public sealed record CreateArticleRequest(string? Author, string? Title, string? Body, IReadOnlyList<string>? Tags);

    /// <summary>POST /api/blogs/message — DM: remitente (opcional) + destinatario + cuerpo.</summary>
    public sealed record SendMessageRequest(string? From, string? To, string? Body);

    /// <summary>POST /api/blogs/saved — guardar un post (owner opcional).</summary>
    public sealed record SaveRequest(string? User, string? PostId);

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
        IReadOnlyList<PostMediaDto> Media,
        DateTime CreatedUtc,
        ReactionsDto Reactions,
        int Comments,
        int Reposts);

    /// <summary>Contrato UI: la app lee `media:[{url,kind,alt}]` (array), no `mediaUrl` (string).</summary>
    public sealed record PostMediaDto(string Url, string Kind, string Alt);

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

    // ── OLA 6 — DMs ────────────────────────────────────────────────

    public sealed record MessagesResponse(IReadOnlyList<DmThreadSummaryDto> Threads);

    public sealed record ThreadResponse(DmThreadDto Thread);

    public sealed record DmThreadSummaryDto(
        string ThreadId,
        IReadOnlyList<AuthorDto> Participants,
        string LastMessagePreview,
        DateTimeOffset LastMessageAt,
        int MessageCount);

    public sealed record DmThreadDto(
        string ThreadId,
        IReadOnlyList<AuthorDto> Participants,
        IReadOnlyList<DmMessageDto> Messages,
        DateTimeOffset LastMessageAt);

    public sealed record DmMessageDto(
        string Id,
        string From,
        string Body,
        DateTimeOffset SentAt);

    // ── OLA 6 — Notificaciones ─────────────────────────────────────

    public sealed record NotificationsResponse(IReadOnlyList<NotificationDto> Notifications);

    public sealed record NotificationDto(
        string Id,
        string Type,
        AuthorDto Actor,
        string? ObjectId,
        string Text,
        DateTime CreatedUtc);

    // ── OLA 6 — Explore / trending ─────────────────────────────────

    public sealed record ExploreResponse(IReadOnlyList<PostDto> Posts, IReadOnlyList<TrendingTagDto> Trending);

    public sealed record TrendingTagDto(string Tag, int Posts, int Score);

    // ── OLA 6 — Guardados ──────────────────────────────────────────

    public sealed record SavedStateResponse(bool Saved, string PostId);

    // ── OLA 6 — Creator Studio ─────────────────────────────────────

    public sealed record StudioResponse(AuthorDto Author, StudioMetricsDto Metrics, IReadOnlyList<StudioPostDto> TopPosts);

    public sealed record StudioMetricsDto(
        int Followers,
        int Following,
        int Posts,
        int Reach,
        double Engagement);

    public sealed record StudioPostDto(
        string Id,
        string Kind,
        string Excerpt,
        int Reactions,
        int Comments);
}
