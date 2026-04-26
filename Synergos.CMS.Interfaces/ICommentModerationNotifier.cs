namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de notificación al equipo moderador cuando un comentario
/// nuevo queda pendiente de aprobación. Permite que el sitio
/// adopte cualquier canal (email, Slack, webhook, queue) sin
/// acoplar a <see cref="ICommentRepository"/> ni al
/// <c>CommentsController</c>.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.EmailCommentModerationNotifier</c> y
/// envía un email a <c>CommentsSettings.NotifyEmailAddress</c> via
/// <see cref="IEmailService"/>. Si esa dirección está vacía/null,
/// el notifier es no-op (no spamea logs).
///
/// Sin async sobre Task — para sitios con volumen alto se puede swap
/// por adapter que enqueue en una cola y devuelva inmediato.
/// </remarks>
public interface ICommentModerationNotifier
{
    /// <summary>
    /// Notifica que un comentario nuevo entró a la cola de moderación
    /// (Approved=false). Llamado fire-and-forget desde el controller —
    /// excepciones del adapter NO deben romper el flow del visitante.
    /// </summary>
    Task NotifyPendingAsync(Comment comment, CancellationToken cancellationToken);
}
