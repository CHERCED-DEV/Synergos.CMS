namespace Synergos.CMS.Interfaces;

// Los tipos compartidos por las tres caras del repositorio de comentarios
// (ICommentReader / ICommentWriter / ICommentModeration). Viven juntos porque son un solo
// vocabulario: el comentario, la referencia con la que se lo nombra y la página de la cola.

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
/// <param name="ParentId">Id del comentario padre cuando este es una
///   respuesta (anidación a 1 nivel — hilo de 2 niveles, ADR 0100).
///   <c>null</c> = comentario top-level. Backward-compat: los
///   comentarios persistidos antes de Ola 230 no tienen el campo en su
///   JSON y deserializan a <c>null</c> ⇒ top-level. Las respuestas a
///   una respuesta se aplanan al hilo del top-level (sin nesting > 2).</param>
/// <param name="Likes">Contador acumulado de reacciones "me gusta".
///   Default 0. Backward-compat: ausente en JSON legacy ⇒ 0.</param>
public sealed record Comment(
    string Id,
    int NodeId,
    string? MemberKey,
    string AuthorName,
    string Body,
    DateTime CreatedAtUtc,
    bool Approved,
    Guid? ParentId = null,
    int Likes = 0);

/// <summary>
/// Datos para crear un comentario nuevo. El repository asigna Id,
/// CreatedAtUtc y decide Approved según política.
/// </summary>
/// <param name="ParentId">Id del comentario top-level al que responde, o
///   <c>null</c> para un comentario nuevo de primer nivel. El repository
///   valida que el padre exista y sea top-level; si el padre apuntado es
///   a su vez una respuesta, la nueva respuesta se re-ancla al abuelo
///   (top-level) para mantener el hilo en 2 niveles. ADR 0100.</param>
public sealed record NewComment(
    int NodeId,
    string? MemberKey,
    string AuthorName,
    string Body,
    Guid? ParentId = null);
