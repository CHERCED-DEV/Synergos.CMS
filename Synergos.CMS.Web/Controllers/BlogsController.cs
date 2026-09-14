using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON de la red social (dominio Blogs — OLA 3). Es el equivalente social
/// del <see cref="ShopCatalogController"/>/<see cref="BookingController"/>: delega
/// el feed/contenido a <see cref="IContentStream"/> (abstracción REUSABLE — la
/// reusa Educación por polimorfismo), el grafo follow a
/// <see cref="ISocialGraphService"/>, las reacciones a <see cref="IReactionService"/>,
/// el perfil a <see cref="ISocialProfileProjection"/>, y los comentarios del post
/// al <see cref="ICommentReader"/> EXISTENTE (no se crea otro — ya hace hilos
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
/// (<c>post-001</c>); <see cref="ICommentReader"/> indexa por <c>int nodeId</c>.
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
    private readonly ICommentReader _comments;
    private readonly IMessagingService _messaging;
    private readonly IUserCollection _collections;
    private readonly INotificationFeed _notifications;

    private readonly IMemberAccessGate _gate;

    public BlogsController(
        IContentStream stream,
        ISocialGraphService graph,
        IReactionService reactions,
        ISocialProfileProjection profiles,
        ICommentReader comments,
        IMessagingService messaging,
        IUserCollection collections,
        INotificationFeed notifications,
        IMemberAccessGate gate)
    {
        _stream = stream;
        _graph = graph;
        _reactions = reactions;
        _profiles = profiles;
        _comments = comments;
        _messaging = messaging;
        _collections = collections;
        _notifications = notifications;
        _gate = gate;
    }


    // ── Identidad server-trusted (molde de Gov/Eventos: ADR 0103) ───────────────
    //
    // Estas rutas tomaban al usuario de un `?user=`/`?author=` o del body. Con DMs eso
    // significa LEER LA BANDEJA DE CUALQUIERA sabiendo su id, y ESCRIBIR haciéndose pasar
    // por otro. Es el mismo patrón que ya se cerró en Tienda, Gobierno y Eventos.

    /// <summary>
    /// Exige sesión y devuelve el id de actor server-trusted. 401 si es anónimo.
    /// </summary>
    private (IActionResult? denied, string actorId) RequireActor()
    {
        var email = _gate.CurrentMemberEmail;
        if (!_gate.IsAuthenticated || string.IsNullOrWhiteSpace(email))
        {
            return (Unauthorized(new { error = "Se requiere iniciar sesión." }), string.Empty);
        }
        return (null, email);
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

        var comments = await ToThreadAsync(_comments.GetApprovedForNode(NodeIdFor(id)), cancellationToken);

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

        // Identidad del GATE. Antes salia del cliente, asi que se podia publicar,
        // reaccionar o seguir A NOMBRE DE OTRO: suplantacion, no solo lectura ajena.
        var (denied, authorId) = RequireActor();
        if (denied is not null) { return denied; }

        ContentStreamItem created;
        try
        {
            created = await _stream.CreateAsync(
                new NewContentItem(
                    AuthorId: authorId,
                    Body: request.Body ?? string.Empty,
                    MediaUrl: request.MediaUrl,
                    Kind: string.IsNullOrWhiteSpace(request.Kind) ? "post" : request.Kind.Trim(),
                    MediaAlt: request.MediaAlt),
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

        // Identidad del GATE. Antes salia del cliente, asi que se podia publicar,
        // reaccionar o seguir A NOMBRE DE OTRO: suplantacion, no solo lectura ajena.
        var (denied, actor) = RequireActor();
        if (denied is not null) { return denied; }

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

        // Identidad del GATE. Antes salia del cliente, asi que se podia publicar,
        // reaccionar o seguir A NOMBRE DE OTRO: suplantacion, no solo lectura ajena.
        var (denied, follower) = RequireActor();
        if (denied is not null) { return denied; }


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
        var viewerFollows = await _graph.IsFollowingAsync(DemoCurrentActor, profile.ActorId, cancellationToken);

        return Ok(new ProfileResponse(
            Author: ToProfileDto(profile) with
            {
                Following = viewerFollows,
                // Los TRES contadores del header se leen del autor, no de `stats`: el
                // borde los tenía y los ponía donde nadie los mira, así que todo
                // perfil mostraba 0 publicaciones, 0 seguidores y 0 siguiendo.
                FollowersCount = counts.Followers,
                FollowingCount = counts.Following,
                PostsCount = posts.Count,
            },
            Posts: posts,
            Stats: new ProfileStatsDto(
                Posts: posts.Count,
                Followers: counts.Followers,
                Following: counts.Following),
            Following: viewerFollows));
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

        // Identidad del GATE. Antes salia del cliente, asi que se podia publicar,
        // reaccionar o seguir A NOMBRE DE OTRO: suplantacion, no solo lectura ajena.
        var (denied, authorId) = RequireActor();
        if (denied is not null) { return denied; }

        // `hashtags` es la clave que declara el contrato de la UI; `tags` la que el
        // borde estrenó. Se aceptan las dos y gana la que venga.
        var tags = request.Tags is { Count: > 0 } ? request.Tags : request.Hashtags;
        var body = ComposeArticleBody(request.Title.Trim(), request.Body.Trim(), tags);

        ContentStreamItem created;
        try
        {
            created = await _stream.CreateAsync(
                new NewContentItem(
                    AuthorId: authorId,
                    Body: body,
                    // La portada que eligió quien escribe. Iba en `null` fijo, así que
                    // el editor largo pedía una imagen y el artículo salía sin ella.
                    MediaUrl: string.IsNullOrWhiteSpace(request.CoverUrl) ? null : request.CoverUrl.Trim(),
                    Kind: "article",
                    MediaAlt: string.IsNullOrWhiteSpace(request.CoverAlt) ? request.Title.Trim() : request.CoverAlt.Trim()),
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
    public async Task<IActionResult> Messages(CancellationToken cancellationToken)
    {
        var (denied, who) = RequireActor();
        if (denied is not null) { return denied; }

        var inbox = await _messaging.GetInboxAsync(who, cancellationToken);

        var threads = new List<DmThreadSummaryDto>(inbox.Count);
        foreach (var t in inbox)
        {
            var participants = await ToParticipants(t.Participants, cancellationToken);
            threads.Add(new DmThreadSummaryDto(
                ThreadId: t.ThreadId,
                Participants: participants,
                LastMessagePreview: t.LastMessagePreview,
                LastMessageAt: t.LastMessageAt,
                MessageCount: t.MessageCount,
                Id: t.ThreadId,
                Participant: OtherParticipant(participants, who),
                LastMessage: t.LastMessagePreview,
                LastAtUtc: t.LastMessageAt));
        }

        return Ok(new MessagesResponse(Threads: threads));
    }

    // GET /api/blogs/thread/{id} → { thread }
    [HttpGet("thread/{id}")]
    public async Task<IActionResult> Thread(string id, CancellationToken cancellationToken)
    {
        var (denied, actorId) = RequireActor();
        if (denied is not null) { return denied; }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del hilo es requerido." });
        }

        var thread = await _messaging.GetThreadAsync(id, cancellationToken);
        if (thread is null)
        {
            return NotFound(new { error = $"Hilo '{id}' no encontrado." });
        }

        // OWNERSHIP: una conversación privada solo la lee quien participa en ella. Sin
        // esto, conocer (o adivinar) el id del hilo bastaba para leer los mensajes de
        // otros dos. `StatusCode(403)` y NO `Forbid()`: con auth de members redirige.
        if (!thread.Participants.Any(p => string.Equals(p, actorId, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "La conversación no es suya." });
        }

        return Ok(new ThreadResponse(Thread: await ToDmThreadDto(thread, actorId, cancellationToken)));
    }

    // POST /api/blogs/message { from, to, body } → { thread }
    // Reusa el IMessagingService genérico con contexto "dm". Idempotente por
    // (contexto + par): re-abrir la misma conversación agrega el mensaje al hilo
    // existente en vez de duplicarlo.
    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] SendMessageRequest? request, CancellationToken cancellationToken)
    {
        // Autenticar ANTES de mirar el cuerpo: no se procesa input de un anónimo.
        var (denied, from) = RequireActor();
        if (denied is not null) { return denied; }

        if (request is null || string.IsNullOrWhiteSpace(request.Body)
            || (string.IsNullOrWhiteSpace(request.To) && string.IsNullOrWhiteSpace(request.ThreadId)))
        {
            return BadRequest(new { error = "El mensaje requiere hilo o destinatario, y cuerpo." });
        }

        // El remitente sale del GATE y se IGNORA `request.From`: antes cualquiera
        // escribía haciéndose pasar por otro con solo poner su id en el body.

        MessageThread thread;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.ThreadId))
            {
                // La vía que usa la app: se contesta DENTRO de la conversación abierta,
                // donde no hay a quién elegir. El destinatario sale del hilo, no del
                // cuerpo — pedírselo al cliente era lo que devolvía 400 siempre.
                var target = await _messaging.GetThreadAsync(request.ThreadId.Trim(), cancellationToken);
                if (target is null)
                {
                    return NotFound(new { error = $"Hilo '{request.ThreadId.Trim()}' no encontrado." });
                }

                // MISMA regla de propiedad que leer el hilo: escribir en una conversación
                // ajena es peor que leerla. 403 y no 400 — la app los distingue, y "no es
                // suya" no se arregla reintentando.
                if (!target.Participants.Any(pp => string.Equals(pp, from, StringComparison.OrdinalIgnoreCase)))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { error = "La conversación no es suya." });
                }

                thread = await _messaging.ReplyAsync(target.ThreadId, from, request.Body, cancellationToken);
            }
            else
            {
                thread = await _messaging.StartThreadAsync(
                    DmContext, from, request.To!.Trim(), request.Body, cancellationToken);
            }
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var dto = await ToDmThreadDto(thread, from, cancellationToken);
        // El mensaje RECIÉN añadido, que es lo que la app lee para pintar la burbuja
        // confirmada. Devolver sólo el hilo la dejaba sintetizándola en local.
        return Ok(new ThreadResponse(Thread: dto, Message: dto.Messages.LastOrDefault()));
    }

    // ── 9. Notificaciones ──────────────────────────────────────────
    // GET /api/blogs/notifications?user= → { notifications:[...] }
    // Eventos dirigidos (follow/reacción/mención) derivados del grafo + reacciones.
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken)
    {
        var (denied, who) = RequireActor();
        if (denied is not null) { return denied; }

        var events = await _notifications.GetForAsync(who, cancellationToken: cancellationToken);

        var dtos = events.Select(n => new NotificationDto(
            Id: n.Id,
            Type: n.Type,
            Verb: MapVerb(n.Type),
            Actor: new AuthorDto(n.Actor.Id, n.Actor.Handle, n.Actor.DisplayName, n.Actor.AvatarUrl, n.Actor.Verified,
                ActorKey: n.Actor.Id),
            ObjectId: n.ObjectId,
            PostId: n.ObjectId,
            Text: n.Text,
            Summary: n.Text,
            CreatedUtc: n.CreatedUtc,
            CreatedAtUtc: n.CreatedUtc)).ToList();

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

        var trending = ComputeTrending(page.Items, await ResolveReactionWeightsAsync(page.Items, cancellationToken));

        IEnumerable<ContentStreamItem> matches = page.Items;
        var query = q?.Trim();
        var hashtag = NormalizeTag(tag);
        if (!string.IsNullOrWhiteSpace(query))
        {
            // Ignora mayúsculas Y tildes (mismo plegado que los 5 catálogos, una sola impl).
            matches = matches.Where(i => CatalogText.Contains(i.Body, query));
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

        // `hashtags` es la clave que la UI lee; `trending` se conserva para los
        // consumers previos. La MISMA lista en las dos.
        return Ok(new ExploreResponse(Posts: posts, Trending: trending, Hashtags: trending));
    }

    // GET /api/blogs/trending → { hashtags:[...] }
    //
    // La app lo pide sola, desde el día uno, para la barra de tendencias, y no
    // existía: 404 → tendencias de ejemplo, SIEMPRE. Degrada en silencio (es un
    // widget auxiliar), así que ni el cartel de datos de ejemplo lo delataba.
    // No hay cálculo nuevo — es el MISMO ranking que ya devuelve `/explore`.
    [HttpGet("trending")]
    public async Task<IActionResult> Trending(CancellationToken cancellationToken)
    {
        var page = await _stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, PageSize: 100), cancellationToken);
        var trending = ComputeTrending(page.Items, await ResolveReactionWeightsAsync(page.Items, cancellationToken));

        return Ok(new TrendingResponse(Hashtags: trending));
    }

    // ── 11. Guardados ──────────────────────────────────────────────
    // GET /api/blogs/saved?user= → { posts:[...] }
    [HttpGet("saved")]
    public async Task<IActionResult> Saved(CancellationToken cancellationToken)
    {
        var (denied, who) = RequireActor();
        if (denied is not null) { return denied; }

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

    // POST /api/blogs/saved { postId } → { saved:true }
    [HttpPost("saved")]
    public async Task<IActionResult> Save([FromBody] SaveRequest? request, CancellationToken cancellationToken)
    {
        // Se me escapó en el barrido T2-Blogs, y era el mismo IDOR de escritura: tomaba
        // `request.User` del body con fallback a un actor de demo, así que cualquiera
        // añadía un post a la colección de OTRO. Su gemelo `DELETE /saved` sí exigía
        // sesión desde entonces — una asimetría que no tenía justificación: si borrar de
        // la lista de alguien necesita ser ese alguien, añadir también.
        var (denied, who) = RequireActor();
        if (denied is not null) { return denied; }

        if (request is null || string.IsNullOrWhiteSpace(request.PostId))
        {
            return BadRequest(new { error = "El postId es requerido." });
        }

        await _collections.AddAsync(who, SavedCollection, request.PostId.Trim(), cancellationToken);
        return Ok(new SavedStateResponse(Saved: true, PostId: request.PostId.Trim()));
    }

    // DELETE /api/blogs/saved?user=&postId= → { saved:false }
    [HttpDelete("saved")]
    public async Task<IActionResult> Unsave(
        [FromQuery] string? postId,
        CancellationToken cancellationToken)
    {
        var (denied, who) = RequireActor();
        if (denied is not null) { return denied; }

        if (string.IsNullOrWhiteSpace(postId))
        {
            return BadRequest(new { error = "El postId es requerido." });
        }

        await _collections.RemoveAsync(who, SavedCollection, postId.Trim(), cancellationToken);
        return Ok(new SavedStateResponse(Saved: false, PostId: postId.Trim()));
    }

    // ── 12. Creator Studio ─────────────────────────────────────────
    // GET /api/blogs/studio?author= → { author, metrics }
    // Métricas del creador: seguidores (grafo), alcance (reacciones+comentarios
    // acumulados de sus posts) y engagement (alcance / posts).
    [HttpGet("studio")]
    public async Task<IActionResult> Studio(CancellationToken cancellationToken)
    {
        var (denied, authorId) = RequireActor();
        if (denied is not null) { return denied; }

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
                Comments: item.Metrics.Comments,
                PostId: item.Id,
                Engagements: state.Total + item.Metrics.Comments));
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
            TopPosts: top,
            // Arriba del todo, o la consola entera se va al mock. Ver StudioResponse.
            Followers: counts.Followers,
            Reach: reach,
            EngagementRate: engagement));
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
            ObjectKind: MapObjectKind(item.Kind),
            Author: new AuthorDto(
                Id: item.Author.Id,
                Handle: item.Author.Handle,
                DisplayName: item.Author.DisplayName,
                AvatarUrl: item.Author.AvatarUrl,
                Verified: item.Author.Verified,
                ActorKey: item.Author.Id),
            Body: item.Body,
            MediaUrl: item.MediaUrl,
            Media: string.IsNullOrWhiteSpace(item.MediaUrl)
                ? System.Array.Empty<PostMediaDto>()
                // El alt de quien publicó gana; el recorte del cuerpo es la red para
                // lo que se publicó antes de que el alt viajara — nunca al revés.
                : new[] { new PostMediaDto(item.MediaUrl!, "image",
                    string.IsNullOrWhiteSpace(item.MediaAlt) ? BuildMediaAlt(item.Body) : item.MediaAlt!) },
            Hashtags: ExtractTags(item.Body),
            Mentions: ExtractMentions(item.Body),
            CreatedUtc: item.CreatedUtc,
            CreatedAtUtc: item.CreatedUtc,
            Reactions: ToReactionsDto(reactions),
            Comments: item.Metrics.Comments,
            CommentCount: item.Metrics.Comments,
            Reposts: item.Metrics.Reposts,
            RepostCount: item.Metrics.Reposts);
    }

    // Vocabulario UI del tipo de objeto del feed: la app espera
    // postPage|lesson|post; el stream emite article|lesson|post|…
    private static string MapObjectKind(string? kind) => (kind?.Trim().ToLowerInvariant()) switch
    {
        "article" => "postPage",
        "lesson" => "lesson",
        _ => "post",
    };

    /// <summary>
    /// Vocabulario UI del verbo de una notificación.
    /// </summary>
    /// <remarks>
    /// El seam emite <c>follow|reaction|comment|mention</c> (strings abiertos, a
    /// propósito) y la app conoce <c>follow|react|mention|reply|repost</c>. Lo que NO
    /// reconoce <b>no falla: cae en <c>mention</c></b>, así que una reacción y un
    /// comentario se leían como menciones — con su icono, su texto y, peor, dentro de
    /// la pestaña «Menciones», que dejaba de significar nada. Es un VOCAB, como el
    /// <c>Rejected → "resuelto"</c> de #104: no rompe, miente.
    /// </remarks>
    private static string MapVerb(string? type) => (type?.Trim().ToLowerInvariant()) switch
    {
        "reaction" => "react",
        "comment" => "reply",
        "follow" => "follow",
        "repost" => "repost",
        "mention" => "mention",
        // Un verbo que no conocemos se manda tal cual: la app ya decide qué hacer con
        // lo que no reconoce, y traducirlo a "mention" acá sería fabricar el defecto
        // que esto viene a cerrar.
        var other => other ?? "mention",
    };

    private static string BuildMediaAlt(string? body)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0) return "Imagen del post";
        return text.Length <= 80 ? text : text[..80].TrimEnd() + "…";
    }

    private static ReactionsDto ToReactionsDto(ReactionState state) => new(
        Total: state.Total,
        CountsByType: state.CountsByType,
        MyReaction: state.MyReaction,
        Counts: state.CountsByType
            .Select(kv => new ReactionCountDto(kv.Key, kv.Value))
            .ToList(),
        Mine: state.MyReaction);

    /// <summary>
    /// Arma el hilo ANIDADO que la UI pinta a partir de la lista plana del nodo.
    /// </summary>
    /// <remarks>
    /// <para>El borde devolvía los aprobados tal cual: padres y respuestas mezclados en
    /// el mismo nivel. La UI lee <c>comment.replies</c>, así que cada respuesta se
    /// pintaba como comentario suelto — el hilo se veía completo y contaba otra
    /// conversación, que es peor que verse vacío.</para>
    /// <para>La anidación es de 2 niveles por contrato (ADR 0100: el repositorio
    /// re-ancla al abuelo), así que basta un pase. Una respuesta cuyo padre no esté en
    /// el lote —moderado, borrado— sube a primer nivel: perderla sería peor.</para>
    /// </remarks>
    private async Task<IReadOnlyList<CommentDto>> ToThreadAsync(
        IReadOnlyList<Comment> approved, CancellationToken cancellationToken)
    {
        var flat = new List<CommentDto>(approved.Count);
        foreach (var c in approved)
        {
            flat.Add(await ToCommentDto(c, cancellationToken));
        }

        var byId = flat.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var children = new Dictionary<string, List<CommentDto>>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<CommentDto>();
        foreach (var c in flat)
        {
            if (c.ParentId is not null && byId.ContainsKey(c.ParentId))
            {
                if (!children.TryGetValue(c.ParentId, out var bucket))
                {
                    bucket = new List<CommentDto>();
                    children[c.ParentId] = bucket;
                }
                bucket.Add(c);
            }
            else
            {
                roots.Add(c);
            }
        }

        return roots
            .Select(r => children.TryGetValue(r.Id, out var kids) ? r with { Replies = kids } : r)
            .ToList();
    }

    private async Task<CommentDto> ToCommentDto(Comment c, CancellationToken cancellationToken)
    {
        var author = await ResolveCommentAuthor(c, cancellationToken);
        return new CommentDto(
            Id: c.Id,
            Author: author,
            Body: c.Body,
            CreatedUtc: c.CreatedAtUtc,
            CreatedAtUtc: c.CreatedAtUtc,
            // Formato "N" — el MISMO con el que el repositorio genera los `Id`. Con el
            // formato con guiones el `parentId` no casaba con el id de ningún
            // comentario del lote, así que no había con qué emparejar la respuesta.
            ParentId: c.ParentId?.ToString("N"),
            Likes: c.Likes,
            LikeCount: c.Likes,
            Replies: Array.Empty<CommentDto>());
    }

    // Resuelve el autor del comentario al AuthorDto (objeto) que la UI lee. Si el
    // comentario trae MemberKey y hay perfil social resoluble, se usa; si no, se
    // deriva un autor del propio comentario (nombre visible + handle-slug), sin
    // inventar verificación ni avatar.
    private async Task<AuthorDto> ResolveCommentAuthor(Comment c, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(c.MemberKey))
        {
            var profile = await _profiles.GetByActorIdAsync(c.MemberKey, cancellationToken);
            if (profile is not null)
            {
                return ToProfileDto(profile);
            }
        }

        var id = string.IsNullOrWhiteSpace(c.MemberKey) ? c.Id : c.MemberKey!;
        return new AuthorDto(
            Id: id,
            Handle: HandleFromName(c.AuthorName),
            DisplayName: string.IsNullOrWhiteSpace(c.AuthorName) ? id : c.AuthorName,
            AvatarUrl: null,
            Verified: false,
            ActorKey: id);
    }

    // Deriva un @handle estable del nombre visible (minúsculas, solo alfanuméricos).
    private static string HandleFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "anon";
        var slug = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return slug.Length == 0 ? "anon" : slug;
    }

    private static AuthorDto ToProfileDto(SocialProfile p) => new(
        Id: p.ActorId,
        Handle: p.Handle,
        DisplayName: p.DisplayName,
        AvatarUrl: p.AvatarUrl,
        Verified: p.Verified,
        ActorKey: p.ActorId,
        // La bio y el banner ya vivían en SocialProfile; el DTO no los llevaba, así
        // que el header del perfil salía sin biografía y sin portada.
        Bio: p.Bio,
        BannerUrl: p.BannerUrl);

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

    private async Task<DmThreadDto> ToDmThreadDto(
        MessageThread thread, string viewerId, CancellationToken cancellationToken)
    {
        var participants = await ToParticipants(thread.Participants, cancellationToken);
        var byId = new Dictionary<string, AuthorDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in participants)
        {
            byId[p.Id] = p;
        }

        var messages = thread.Messages.Select(m => new DmMessageDto(
            Id: m.MessageId,
            From: m.From,
            Body: m.Body,
            SentAt: m.SentAt,
            // El autor va como OBJETO: el id suelto no le sirve al normalizador, que
            // descartaba cada mensaje y dejaba la conversación en blanco.
            Author: byId.TryGetValue(m.From, out var a) ? a : SyntheticAuthor(m.From),
            CreatedAtUtc: m.SentAt,
            Outgoing: string.Equals(m.From, viewerId, StringComparison.OrdinalIgnoreCase),
            ThreadId: thread.ThreadId)).ToList();

        return new DmThreadDto(
            ThreadId: thread.ThreadId,
            Participants: participants,
            Messages: messages,
            LastMessageAt: thread.LastMessageAt,
            Id: thread.ThreadId,
            Participant: OtherParticipant(participants, viewerId));
    }

    /// <summary>
    /// El otro lado de una conversación 1:1. Quien mira no se lista a sí mismo; si el
    /// hilo no lo incluye (no debería: la propiedad se comprueba antes), se devuelve el
    /// primero antes que <c>null</c> — una fila sin participante la UI la descarta entera.
    /// </summary>
    private static AuthorDto? OtherParticipant(IReadOnlyList<AuthorDto> participants, string viewerId)
        => participants.FirstOrDefault(p => !string.Equals(p.Id, viewerId, StringComparison.OrdinalIgnoreCase))
           ?? participants.FirstOrDefault();

    /// <summary>
    /// El peso de reacciones de cada post del lote, resuelto de una vez.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe para sacar el <c>await</c> del ranking.</b> Antes el cálculo de trending
    /// hacía <c>GetStateAsync(...).GetAwaiter().GetResult()</c> DENTRO de un bucle sobre hasta
    /// 100 posts: bloquear un hilo del pool cien veces por request en el endpoint más público
    /// del vertical, que es la receta conocida de inanición del pool bajo carga.</para>
    ///
    /// <para>Se resuelven en secuencia y no con <c>Task.WhenAll</c> a propósito: la impl viva
    /// del seam lee de disco, y cien lecturas concurrentes cambiarían un problema de hilos por
    /// uno de I/O. Si algún día el seam va contra algo que sí paraleliza, el cambio es local a
    /// este método.</para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, int>> ResolveReactionWeightsAsync(
        IReadOnlyList<ContentStreamItem> items,
        CancellationToken cancellationToken)
    {
        var weights = new Dictionary<string, int>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (weights.ContainsKey(item.Id))
            {
                continue;
            }
            var state = await _reactions.GetStateAsync(item.Id, cancellationToken: cancellationToken);
            weights[item.Id] = state.Total;
        }
        return weights;
    }

    /// <summary>
    /// Hashtags trending: frecuencia de cada <c>#tag</c> en el feed, ponderada por las
    /// reacciones del post que lo contiene.
    /// </summary>
    /// <remarks>
    /// <b>Pura y con los pesos ya resueltos</b>, que es lo que la vuelve verificable: el ranking
    /// —el peso base de 1, el desempate alfabético, el tope de 10— era lógica de producto
    /// enterrada detrás de una llamada de I/O. Un post sin peso conocido cuenta como 0
    /// reacciones y no desaparece del conteo: no aparecer sería peor que aparecer al fondo.
    /// </remarks>
    internal static IReadOnlyList<TrendingTagDto> ComputeTrending(
        IReadOnlyList<ContentStreamItem> items,
        IReadOnlyDictionary<string, int> reactionWeights)
    {
        var scores = new Dictionary<string, (int Posts, int Weight)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            // El +1 es el peso del post en sí: un tag en un post sin reacciones sigue contando.
            var weight = 1 + (reactionWeights.TryGetValue(item.Id, out var total) ? total : 0);
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
            .Select(kv => new TrendingTagDto(
                Tag: kv.Key, Posts: kv.Value.Posts, Score: kv.Value.Weight, PostCount: kv.Value.Posts))
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

    // Extrae las menciones (@handle) del cuerpo, sin el prefijo @, en minúsculas.
    // Gemelo exacto de ExtractTags: el contrato declara las dos y el borde derivaba
    // una sola.
    private static IReadOnlyList<string> ExtractMentions(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        var handles = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(body, @"@(\w[\w-]*)");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            handles.Add(m.Groups[1].Value.ToLowerInvariant());
        }
        return handles;
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
        => new(Id: actorId, Handle: actorId, DisplayName: actorId, AvatarUrl: null, Verified: false,
            ActorKey: actorId);

    // nodeId determinista a partir del id string del post (FNV-1a 32-bit, forzado
    // positivo) para reusar el ICommentReader (indexado por int) sin schema nuevo.
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
    /// <param name="MediaAlt">
    /// El texto alternativo que escribi&#243; quien publica. El compositor lo PIDE y lo
    /// manda, y el record no lo declaraba: System.Text.Json lo descartaba sin decir
    /// nada, as&#237; que el alt de cada imagen acababa siendo un recorte del cuerpo.
    /// </param>
    public sealed record CreatePostRequest(string? Body, string? MediaUrl, string? AuthorId, string? Kind, string? MediaAlt = null);

    /// <summary>POST /api/blogs/post/{id}/react — el tipo de reacción.</summary>
    public sealed record ReactRequest(string? Type);

    /// <summary>POST /api/blogs/article — artículo long-form (Kind=article).</summary>
    /// <param name="CoverUrl">
    /// La portada del art&#237;culo. El editor largo la pide en su propio campo y la
    /// manda; el record no la declaraba, as&#237; que el art&#237;culo se publicaba SIN
    /// portada y sin que nada fallara — y el modo demo s&#237; la pintaba, porque el
    /// post sintetizado en local s&#237; la usa.
    /// </param>
    /// <param name="Hashtags">Alias de <paramref name="Tags"/>: es la clave que declara el contrato de la UI.</param>
    public sealed record CreateArticleRequest(
        string? Author,
        string? Title,
        string? Body,
        IReadOnlyList<string>? Tags,
        string? CoverUrl = null,
        string? CoverAlt = null,
        IReadOnlyList<string>? Hashtags = null);

    /// <summary>POST /api/blogs/message — DM: remitente (opcional) + destinatario + cuerpo.</summary>
    /// <param name="ThreadId">
    /// El hilo al que se responde. <b>Es la forma que manda la app</b> —un DM se
    /// contesta desde la conversaci&#243;n abierta, donde no hay a qui&#233;n elegir— y el
    /// record s&#243;lo declaraba <c>To</c>: el destinatario llegaba vac&#237;o y el
    /// endpoint contestaba <b>400 siempre</b>. El cliente lo tapaba sintetizando la
    /// burbuja, as&#237; que el mensaje se ve&#237;a enviado y no exist&#237;a en ninguna parte.
    /// Se aceptan las dos formas: con hilo se responde en &#233;l; con <c>To</c> se abre
    /// (o se retoma) el de esa pareja.
    /// </param>
    public sealed record SendMessageRequest(string? From, string? To, string? Body, string? ThreadId = null);

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

    /// <param name="Following">
    /// Contrato UI: <c>normalizeProfile</c> lee <c>following</c> en la RAÍZ de la
    /// respuesta, no dentro de <c>author</c>. Anidado se descartaba, así que el
    /// botón del perfil decía «Seguir» sobre alguien a quien ya se seguía — y al
    /// pulsarlo dejaba de seguirlo. Se emite en los dos sitios.
    /// </param>
    public sealed record ProfileResponse(
        AuthorDto Author,
        IReadOnlyList<PostDto> Posts,
        ProfileStatsDto Stats,
        bool Following = false);

    public sealed record PostDto(
        string Id,
        string Kind,
        // Contrato UI: la app lee `objectKind` (postPage|lesson|post), no `kind` (article).
        string ObjectKind,
        AuthorDto Author,
        string Body,
        string? MediaUrl,
        IReadOnlyList<PostMediaDto> Media,
        // Contrato UI: `hashtags:[...]` derivado del cuerpo (#tag).
        IReadOnlyList<string> Hashtags,
        // Contrato UI: `mentions:[...]` (@handle) — la MISMA derivación que los
        // hashtags, sobre el mismo cuerpo. Se emitía una y no la otra.
        IReadOnlyList<string> Mentions,
        DateTime CreatedUtc,
        // Contrato UI: la app lee `createdAtUtc`, no `createdUtc` (o muestra "ahora").
        DateTime CreatedAtUtc,
        ReactionsDto Reactions,
        int Comments,
        // Contrato UI: la app lee `commentCount`/`repostCount`, no `comments`/`reposts`.
        int CommentCount,
        int Reposts,
        int RepostCount);

    /// <summary>Contrato UI: la app lee `media:[{url,kind,alt}]` (array), no `mediaUrl` (string).</summary>
    public sealed record PostMediaDto(string Url, string Kind, string Alt);

    public sealed record AuthorDto(
        string Id,
        string Handle,
        string DisplayName,
        string? AvatarUrl,
        bool Verified,
        // Contrato UI (solo header de perfil): ¿el viewer sigue a este autor?
        // Defaultea a false para no romper los otros call-sites de AuthorDto
        // (post/notificación/participante), donde el dato no aplica.
        bool Following = false,
        // Contrato UI: `normalizeAuthor` resuelve `actorKey` ?? `id`. La canónica es
        // la primera; `id` funcionaba por el `??` — la red de seguridad, no el
        // arreglo (ADR 0083). Misma clave que el resto de la app usa para seguir,
        // abrir perfil y emitir `authorfollowed`.
        string? ActorKey = null,
        // Contrato UI (header de perfil): la app pinta `author.bio` y los TRES
        // contadores del header desde el AUTOR, no desde `stats` —que el borde sí
        // emitía y nadie lee—. Nulos/cero en los call-sites donde no aplica
        // (post/notificación/participante), que es la verdad sobre ellos.
        string? Bio = null,
        string? BannerUrl = null,
        int FollowersCount = 0,
        int FollowingCount = 0,
        int PostsCount = 0);

    public sealed record ReactionsDto(
        int Total,
        IReadOnlyDictionary<string, int> CountsByType,
        string? MyReaction,
        // Contrato UI: `reactions.counts:[{type,count}]` (array) y `reactions.mine`.
        IReadOnlyList<ReactionCountDto> Counts,
        string? Mine);

    /// <summary>Contrato UI: un conteo de reacciones por tipo — `{type,count}`.</summary>
    public sealed record ReactionCountDto(string Type, int Count);

    public sealed record CommentDto(
        string Id,
        // Contrato UI: `comment.author` es un OBJETO {id,handle,displayName,avatarUrl,
        // verified}, no un string — un string se descarta y el hilo queda vacío.
        AuthorDto Author,
        string Body,
        DateTime CreatedUtc,
        // Contrato UI: la app lee `createdAtUtc` y `likeCount`.
        DateTime CreatedAtUtc,
        string? ParentId,
        int Likes,
        int LikeCount,
        // Contrato UI: el hilo se pinta ANIDADO (`comment.replies`), no plano. El
        // borde devolvía la lista plana del nodo con su `parentId`, así que cada
        // respuesta salía como comentario de primer nivel: el hilo se veía, pero
        // contaba otra conversación. La anidación es de 2 niveles (ADR 0100).
        IReadOnlyList<CommentDto> Replies);

    public sealed record ProfileStatsDto(int Posts, int Followers, int Following);

    // ── OLA 6 — DMs ────────────────────────────────────────────────

    public sealed record MessagesResponse(IReadOnlyList<DmThreadSummaryDto> Threads);

    /// <param name="Message">
    /// Contrato UI: <c>sendMessage</c> lee <c>message</c> (el mensaje reci&#233;n
    /// a&#241;adido), no el hilo entero. Sin &#233;l el normalizador devolv&#237;a
    /// <c>null</c> y el cliente sintetizaba la burbuja en local — la misma forma
    /// de #104: un env&#237;o que no ocurri&#243; se ve igual que uno que s&#237;.
    /// </param>
    public sealed record ThreadResponse(DmThreadDto Thread, DmMessageDto? Message = null);

    /// <remarks>
    /// <b>Cada clave nueva de aqu&#237; es la diferencia entre una bandeja llena y una
    /// vac&#237;a.</b> <c>normalizeThread</c> exige <c>id</c> Y <c>participant</c> y
    /// DESCARTA la fila entera si falta cualquiera de los dos; el borde emit&#237;a
    /// <c>threadId</c> y <c>participants</c> (plural, la lista cruda), as&#237; que las
    /// descartaba todas y devolv&#237;a <c>[]</c> — una respuesta v&#225;lida, sin error
    /// y sin cartel de datos de ejemplo.
    /// </remarks>
    public sealed record DmThreadSummaryDto(
        string ThreadId,
        IReadOnlyList<AuthorDto> Participants,
        string LastMessagePreview,
        DateTimeOffset LastMessageAt,
        int MessageCount,
        string? Id = null,
        // El OTRO participante, ya resuelto a autor: una conversaci&#243;n 1:1 se
        // lista por con qui&#233;n es, y quien mira no se lista a s&#237; mismo.
        AuthorDto? Participant = null,
        string? LastMessage = null,
        DateTimeOffset? LastAtUtc = null);

    public sealed record DmThreadDto(
        string ThreadId,
        IReadOnlyList<AuthorDto> Participants,
        IReadOnlyList<DmMessageDto> Messages,
        DateTimeOffset LastMessageAt,
        string? Id = null,
        AuthorDto? Participant = null);

    /// <remarks>
    /// <c>normalizeMessage</c> exige <c>id</c> Y <c>author</c> (un OBJETO), y el borde
    /// emit&#237;a <c>from</c> (un id suelto): cada mensaje se descartaba, as&#237; que la
    /// conversaci&#243;n abierta sal&#237;a vac&#237;a. <c>outgoing</c> es lo que alinea la
    /// burbuja a la derecha — sin &#233;l, los mensajes propios se ven como ajenos.
    /// </remarks>
    public sealed record DmMessageDto(
        string Id,
        string From,
        string Body,
        DateTimeOffset SentAt,
        AuthorDto? Author = null,
        DateTimeOffset? CreatedAtUtc = null,
        bool Outgoing = false,
        string? ThreadId = null);

    // ── OLA 6 — Notificaciones ─────────────────────────────────────

    public sealed record NotificationsResponse(IReadOnlyList<NotificationDto> Notifications);

    public sealed record NotificationDto(
        string Id,
        string Type,
        // Contrato UI: la app lee `verb` (= tipo del evento).
        string Verb,
        AuthorDto Actor,
        string? ObjectId,
        // Contrato UI: la app lee `postId` (= objeto referenciado).
        string? PostId,
        string Text,
        // Contrato UI: la app lee `summary`, no `text`.
        string Summary,
        DateTime CreatedUtc,
        // Contrato UI: la app lee `createdAtUtc`; con `createdUtc` a secas caía al
        // `new Date()` del normalizador y TODA notificación decía "ahora".
        DateTime? CreatedAtUtc = null);

    // ── OLA 6 — Explore / trending ─────────────────────────────────

    /// <param name="Hashtags">
    /// Contrato UI: <c>normalizeSearch</c> lee <c>hashtags</c>. El borde emit&#237;a la
    /// MISMA lista bajo <c>trending</c>, as&#237; que la pesta&#241;a de hashtags de
    /// explorar sal&#237;a vac&#237;a con el servidor lleno. Las dos claves llevan lo mismo.
    /// </param>
    public sealed record ExploreResponse(
        IReadOnlyList<PostDto> Posts,
        IReadOnlyList<TrendingTagDto> Trending,
        IReadOnlyList<TrendingTagDto>? Hashtags = null);

    /// <summary>La bandeja de tendencias — <c>GET /trending</c>, que la app pide sola.</summary>
    public sealed record TrendingResponse(IReadOnlyList<TrendingTagDto> Hashtags);

    /// <param name="PostCount">
    /// Contrato UI: el contador de la faceta de tendencias. <c>posts</c> no lo lee
    /// nadie, as&#237; que cada tendencia sal&#237;a con un 0 al lado.
    /// </param>
    public sealed record TrendingTagDto(string Tag, int Posts, int Score, int PostCount = 0);

    // ── OLA 6 — Guardados ──────────────────────────────────────────

    public sealed record SavedStateResponse(bool Saved, string PostId);

    // ── OLA 6 — Creator Studio ─────────────────────────────────────

    /// <remarks>
    /// <b>Las tres cifras van en la RA&#205;Z y no s&#243;lo en <c>metrics</c>, y de eso depende
    /// que el estudio exista.</b> <c>normalizeStudio</c> devuelve <c>null</c> cuando no
    /// encuentra <c>followers</c> ni <c>reach</c> arriba del todo, y ese <c>null</c> manda
    /// la consola ENTERA al mock — con su cartel de datos de ejemplo — aunque el servidor
    /// traiga los n&#250;meros buenos. Es el defecto de la consola del instructor de #102,
    /// calcado. <c>metrics</c> se conserva para los consumers previos.
    /// </remarks>
    public sealed record StudioResponse(
        AuthorDto Author,
        StudioMetricsDto Metrics,
        IReadOnlyList<StudioPostDto> TopPosts,
        int Followers = 0,
        int Reach = 0,
        double EngagementRate = 0d);

    public sealed record StudioMetricsDto(
        int Followers,
        int Following,
        int Posts,
        int Reach,
        double Engagement);

    /// <param name="PostId">
    /// Contrato UI: <c>normalizeTopPosts</c> exige <c>postId</c> y descarta la fila sin
    /// &#233;l — la tabla de contenido top sal&#237;a vac&#237;a. Mismo valor que <c>id</c>.
    /// </param>
    /// <param name="Engagements">
    /// Reacciones + comentarios del post. Es la MISMA suma con la que el propio endpoint
    /// calcula <c>reach</c>, aplicada a una fila; no hay dato nuevo detr&#225;s.
    /// <c>impressions</c> NO se emite: no hay seam que cuente vistas y un n&#250;mero
    /// inventado en una tabla de m&#233;tricas es peor que una columna en cero.
    /// </param>
    public sealed record StudioPostDto(
        string Id,
        string Kind,
        string Excerpt,
        int Reactions,
        int Comments,
        string? PostId = null,
        int Engagements = 0);
}
