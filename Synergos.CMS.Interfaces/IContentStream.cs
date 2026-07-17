namespace Synergos.CMS.Interfaces;

/// <summary>
/// ABSTRACCIÓN REUSABLE de feed/contenido (dominio Blogs — red social, OLA 3).
/// Modela un stream paginado por cursor de <see cref="ContentStreamItem"/>
/// (Actor → Verb → Object, inspirado en ActivityStreams 2.0) y la creación de
/// items nuevos. Es la pieza del MOTOR que resuelve "qué hay en el feed" para la
/// home/perfil/hashtag, equivalente social del <see cref="IProductCatalogProvider"/>
/// (Tienda) o el <see cref="IRoomAvailabilityProvider"/> (Hoteles).
/// </summary>
/// <remarks>
/// <para>
/// <b>Genérica por diseño — reusable por POLIMORFISMO.</b> El item tiene un
/// <see cref="ContentStreamItem.Kind"/> (<c>post</c> | <c>article</c> |
/// <c>lesson</c> | …) para que distintos dominios compartan el feed SIN
/// instanciarse entre sí (DIP, ADR 0002): un post social, un post editorial
/// (<c>postPage</c>) y una "lección publicada" de Educación son todos
/// <see cref="ContentStreamItem"/> con distinto <c>Kind</c>. <b>Educación reusa
/// este seam</b> filtrando <see cref="FeedQuery.Kind"/> = <c>lesson</c> /
/// <c>course-update</c> — depende de la abstracción, no de Blogs; no copia su
/// schema ni instancia su módulo.
/// </para>
/// <para>
/// Seam stub-first (igual que el resto del motor): el default
/// <c>StubContentStream</c> (Application, lógica pura/determinista) sirve un feed
/// sembrado en memoria (varios autores × posts) para que la demo corra
/// end-to-end; el adapter real (índice Examine sobre <c>postPage</c> o un store
/// dedicado de actividad) implementa la misma seam y se registra en su lugar vía
/// el composer sin tocar el módulo Angular ni el controller. ADR 0002
/// (Application sin Umbraco) + ADR 0075 (seam con tests).
/// </para>
/// </remarks>
public interface IContentStream
{
    /// <summary>
    /// Devuelve una página del feed según <paramref name="query"/> (scope +
    /// filtros + cursor) y el cursor de la página siguiente
    /// (<see cref="ContentStreamPage.NextCursor"/> = <c>null</c> cuando no hay
    /// más). Nunca lanza por filtro vacío: feed vacío devuelve
    /// <c>Items = []</c> + <c>NextCursor = null</c>.
    /// </summary>
    Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve el detalle de un item por su id (post + payload + métricas).
    /// Devuelve <c>null</c> si el item no existe.
    /// </summary>
    Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un item nuevo en el stream. El stream asigna
    /// <see cref="ContentStreamItem.Id"/> y <see cref="ContentStreamItem.CreatedUtc"/>
    /// — el caller solo provee el contenido (autor + cuerpo + media + kind).
    /// Devuelve el item persistido con sus metadata.
    /// </summary>
    Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default);
}

/// <summary>
/// Alcance/scope del feed a consultar. Mantiene la abstracción genérica: Blogs
/// usa <see cref="ForYou"/>/<see cref="Following"/>; cualquier dominio
/// (Educación) puede consultar por autor/kind.
/// </summary>
public enum FeedScope
{
    /// <summary>Feed rankeado de descubrimiento ("Para ti"). Default.</summary>
    ForYou = 0,

    /// <summary>Feed derivado del grafo social (autores que <c>AuthorId</c> sigue).</summary>
    Following = 1,

    /// <summary>Feed de un autor concreto (su perfil) — requiere <see cref="FeedQuery.AuthorId"/>.</summary>
    Author = 2,
}

/// <summary>
/// Filtros de la consulta del feed. Todos opcionales salvo el scope (default
/// <see cref="FeedScope.ForYou"/>). <see cref="Cursor"/> pagina (opaco,
/// devuelto por la página anterior). <see cref="AuthorId"/> es el actor de
/// contexto: el "yo" para <see cref="FeedScope.Following"/>, o el dueño del
/// perfil para <see cref="FeedScope.Author"/>. <see cref="Kind"/> filtra por
/// tipo de contenido (clave del polimorfismo: Educación pasa <c>lesson</c>).
/// </summary>
public sealed record FeedQuery(
    FeedScope Scope = FeedScope.ForYou,
    string? Cursor = null,
    string? AuthorId = null,
    string? Kind = null,
    int PageSize = 20);

/// <summary>
/// Página del feed: los items + el cursor de la siguiente página
/// (<c>null</c> = no hay más). El cursor es opaco para el caller — solo se
/// reenvía tal cual en la <see cref="FeedQuery.Cursor"/> siguiente.
/// </summary>
public sealed record ContentStreamPage(
    IReadOnlyList<ContentStreamItem> Items,
    string? NextCursor);

/// <summary>
/// Un item del stream (Actor → Verb → Object). Unidad polimórfica del feed:
/// distintos dominios producen items con distinto <see cref="Kind"/> sobre la
/// misma forma. Lo renderiza la <c>post-card</c> del módulo Angular.
/// </summary>
/// <param name="Id">Id único del item (asignado por el stream).</param>
/// <param name="Kind">Tipo de contenido: <c>post</c> | <c>article</c> |
///   <c>lesson</c> | <c>course-update</c> … — clave del polimorfismo entre
///   dominios.</param>
/// <param name="Author">Autor del item (proyección del Member).</param>
/// <param name="Body">Cuerpo del item (texto plano / markdown corto).</param>
/// <param name="MediaUrl">URL de la media adjunta (imagen/video), o
///   <c>null</c>.</param>
/// <param name="CreatedUtc">Timestamp UTC de creación.</param>
/// <param name="Metrics">Métricas agregadas (reacciones, comentarios,
///   reposts).</param>
public sealed record ContentStreamItem(
    string Id,
    string Kind,
    ContentAuthor Author,
    string Body,
    string? MediaUrl,
    DateTime CreatedUtc,
    ContentMetrics Metrics);

/// <summary>
/// Autor de un item — proyección liviana del Member para el feed (lo rico vive
/// en <see cref="SocialProfile"/>). Desacopla <see cref="Id"/> (actorKey) del
/// MemberKey por la misma razón que patientKey↔MemberKey en healthcare.
/// </summary>
public sealed record ContentAuthor(
    string Id,
    string Handle,
    string DisplayName,
    string? AvatarUrl,
    bool Verified = false);

/// <summary>
/// Métricas agregadas de un item del feed. <see cref="Reactions"/> es el total
/// de reacciones (todos los tipos); <see cref="Comments"/>/<see cref="Reposts"/>
/// los respectivos conteos.
/// </summary>
public sealed record ContentMetrics(
    int Reactions = 0,
    int Comments = 0,
    int Reposts = 0);

/// <summary>
/// Datos para crear un item nuevo. El stream asigna Id + CreatedUtc. El
/// <see cref="Kind"/> default es <c>post</c> (red social); Educación pasa
/// <c>lesson</c>. <see cref="AuthorId"/> es el actor que publica.
/// </summary>
public sealed record NewContentItem(
    string AuthorId,
    string Body,
    string? MediaUrl = null,
    string Kind = "post");
