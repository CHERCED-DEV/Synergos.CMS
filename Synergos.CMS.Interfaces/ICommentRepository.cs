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
