namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de persistencia de comentarios de visitantes/miembros sobre
/// nodos publicados (typically blog posts). Aísla a controllers/
/// renderers del backend de storage (filesystem JSON, SQL, queue
/// hacia un servicio externo de moderación).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemCommentRepository</c> y
/// persiste un JSON por nodo bajo
/// <c>{ContentRoot}/{CommentsSettings.StorageRoot}/{nodeId}.json</c>.
///
/// Para producción con moderación + spam filter (Akismet, custom):
/// swap por adapter que enqueue a un workflow de aprobación. La seam
/// no asume in-line approval — los comentarios pueden persistirse con
/// <see cref="Comment.Approved"/> false y aparecer solo cuando un
/// moderator los aprueba.
/// </remarks>
public interface ICommentRepository
{
    /// <summary>
    /// Devuelve los comentarios aprobados para el nodo, ordenados
    /// por <see cref="Comment.CreatedAtUtc"/> ascendente (más viejo
    /// primero — orden natural de hilo).
    /// </summary>
    IReadOnlyList<Comment> GetApprovedForNode(int nodeId);

    /// <summary>
    /// Persiste un comentario nuevo. <see cref="Comment.Id"/> y
    /// <see cref="Comment.CreatedAtUtc"/> son rellenados por el
    /// repository — el caller solo provee el contenido. Devuelve el
    /// comentario persistido con sus metadata.
    /// </summary>
    Task<Comment> AddAsync(NewComment comment, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve los comentarios pendientes de moderación
    /// (Approved=false) para el nodo, ordenados por
    /// <see cref="Comment.CreatedAtUtc"/> descendente (más nuevo
    /// primero — orden natural de cola moderation).
    /// </summary>
    IReadOnlyList<Comment> GetPendingForNode(int nodeId);

    /// <summary>
    /// Devuelve los comentarios pendientes de moderación a través de
    /// todos los nodos persistidos, ordenados por
    /// <see cref="Comment.CreatedAtUtc"/> descendente. Cap por
    /// <paramref name="limit"/> para evitar payloads enormes en
    /// cola muy abultada.
    /// </summary>
    IReadOnlyList<Comment> GetAllPending(int limit);

    /// <summary>
    /// Aprueba un comentario existente identificado por
    /// <paramref name="nodeId"/> + <paramref name="commentId"/> —
    /// flippea <see cref="Comment.Approved"/> a true. Devuelve true
    /// si se encontró y se actualizó; false si no existe.
    /// </summary>
    Task<bool> ApproveAsync(int nodeId, string commentId, CancellationToken cancellationToken);

    /// <summary>
    /// Elimina un comentario existente del store. Devuelve true si se
    /// eliminó, false si no existía. Usado para spam confirmado o
    /// rechazo definitivo desde el moderation queue.
    /// </summary>
    Task<bool> RejectAsync(int nodeId, string commentId, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve una página de comentarios pendientes (paginated +
    /// filtrable). Ordenados por <see cref="Comment.CreatedAtUtc"/>
    /// descendente. <paramref name="page"/> es 1-based.
    /// <paramref name="nodeIdFilter"/> opcional limita a un nodo
    /// específico (null = todos).
    /// </summary>
    PendingCommentsPage GetPendingPage(int page, int pageSize, int? nodeIdFilter = null);

    /// <summary>
    /// Aprueba una lista de comentarios. Devuelve el número de
    /// items realmente actualizados (excluye no-encontrados o ya
    /// aprobados).
    /// </summary>
    Task<int> BulkApproveAsync(IReadOnlyList<CommentRef> refs, CancellationToken cancellationToken);

    /// <summary>
    /// Rechaza (elimina del store) una lista de comentarios. Devuelve
    /// el número de items realmente eliminados.
    /// </summary>
    Task<int> BulkRejectAsync(IReadOnlyList<CommentRef> refs, CancellationToken cancellationToken);

    /// <summary>
    /// Lee los snapshots completos de los comentarios indicados (incluye
    /// approved=false). Usado por el dashboard para capturar el estado
    /// antes de un bulk-reject + permitir undo.
    /// </summary>
    IReadOnlyList<Comment> ReadByRefs(IReadOnlyList<CommentRef> refs);

    /// <summary>
    /// Restaura una lista de comentarios al store. Idempotente — si ya
    /// existe un item con el mismo Id en el mismo nodo, NO se duplica.
    /// Usado para undo bulk-reject (Ola 124).
    /// </summary>
    Task<int> RestoreAsync(IReadOnlyList<Comment> items, CancellationToken cancellationToken);
}

/// <summary>
/// Referencia compuesta a un comentario (nodo + id). Necesario porque
/// commentId solo es único dentro de un nodo.
/// </summary>
public sealed record CommentRef(int NodeId, string CommentId);

/// <summary>
/// Página paginada de comentarios pendientes — incluye los items, el
/// nro de página actual, el page size y el total absoluto para
/// calcular total pages.
/// </summary>
public sealed record PendingCommentsPage(
    IReadOnlyList<Comment> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}

/// <summary>
/// Comentario persistido.
/// </summary>
/// <param name="Id">UUID generado por el repository.</param>
/// <param name="NodeId">Id del IPublishedContent comentado.</param>
/// <param name="MemberKey">Identificador del miembro autor (opcional —
///   <c>null</c> permite comentarios anónimos si la política lo
///   habilita).</param>
/// <param name="AuthorName">Nombre visible del autor.</param>
/// <param name="Body">Texto del comentario (plain text, sin HTML).</param>
/// <param name="CreatedAtUtc">Timestamp UTC de creación.</param>
/// <param name="Approved">Si false, el comentario existe pero no se
///   muestra en el render. Default true en la impl FileSystem (sin
///   moderación in-line). Adapter de moderación puede flipear a
///   false al crear.</param>
public sealed record Comment(
    string Id,
    int NodeId,
    string? MemberKey,
    string AuthorName,
    string Body,
    DateTime CreatedAtUtc,
    bool Approved);

/// <summary>
/// Datos para crear un comentario nuevo. El repository asigna Id,
/// CreatedAtUtc y decide Approved según política.
/// </summary>
public sealed record NewComment(
    int NodeId,
    string? MemberKey,
    string AuthorName,
    string Body);
