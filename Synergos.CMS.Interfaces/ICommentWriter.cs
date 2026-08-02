namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lo que puede hacer un VISITANTE sobre el hilo: comentar y reaccionar.
/// </summary>
/// <remarks>
/// Las dos operaciones que se alcanzan sin ser moderador. Ninguna decide si algo se publica —
/// eso es <see cref="ICommentModeration"/>—: un comentario nuevo nace pendiente si la
/// configuración lo pide, y un "me gusta" solo aplica sobre lo que ya está aprobado.
/// </remarks>
public interface ICommentWriter
{
    /// <summary>
    /// Persiste un comentario nuevo. <see cref="Comment.Id"/> y
    /// <see cref="Comment.CreatedAtUtc"/> son rellenados por el
    /// repository — el caller solo provee el contenido. Devuelve el
    /// comentario persistido con sus metadata.
    /// </summary>
    Task<Comment> AddAsync(NewComment comment, CancellationToken cancellationToken);

    /// <summary>
    /// Incrementa en uno el contador de "me gusta" de un comentario
    /// existente (Ola 230, ADR 0100). Devuelve el comentario actualizado,
    /// o <c>null</c> si no existe (o no está aprobado — no se reacciona
    /// a comentarios en cola de moderación). No idempotente por diseño:
    /// cada POST suma uno (sin tracking de identidad de quien reacciona).
    /// </summary>
    Task<Comment?> LikeAsync(int nodeId, string commentId, CancellationToken cancellationToken);
}
