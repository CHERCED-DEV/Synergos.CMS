namespace Synergos.CMS.Interfaces;

/// <summary>
/// La cara de MODERACIÓN: ver la cola, aprobar, rechazar y deshacer.
/// </summary>
/// <remarks>
/// <para>Todo lo que hay aquí exige rol, y todo lo que hay aquí ve comentarios <b>sin
/// aprobar</b>. Esas dos frases son la razón de que sea un tipo aparte: quien recibe esta
/// dependencia está declarando que trabaja con contenido que el público todavía no puede ver.
/// En <see cref="ICommentReader"/> eso es imposible por construcción.</para>
///
/// <para><b>Por qué lectura y escritura juntas aquí.</b> No se parte más porque no hay
/// consumidor que lo pida: los dos que la usan —la consola de moderación y el dashboard de
/// admin— leen la cola para poder actuar sobre ella. Partirla en "leer la cola" y "actuar sobre
/// la cola" daría dos tipos que siempre viajan juntos, que es la abstracción prematura que el
/// proyecto evita a propósito.</para>
/// </remarks>
public interface ICommentModeration
{
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
    /// Devuelve una página de comentarios pendientes (paginated +
    /// filtrable). Ordenados por <see cref="Comment.CreatedAtUtc"/>
    /// descendente. <paramref name="page"/> es 1-based.
    /// <paramref name="nodeIdFilter"/> opcional limita a un nodo
    /// específico (null = todos).
    /// </summary>
    PendingCommentsPage GetPendingPage(int page, int pageSize, int? nodeIdFilter = null);

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
