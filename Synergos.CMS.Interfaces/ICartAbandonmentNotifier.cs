namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de notificación cuando el background scanner detecta un cart
/// abandonado (Ola 102). Pareja conceptual de
/// <see cref="ICommentModerationNotifier"/> y
/// <see cref="IFormSubmissionNotifier"/>.
/// </summary>
/// <remarks>
/// Pattern Composite + Channel:
///
/// - <see cref="ICartAbandonmentNotifier"/> es la fachada que el
///   <c>CartAbandonmentScannerHostedService</c> inyecta.
/// - <see cref="ICartAbandonmentNotifierChannel"/> son los canales
///   individuales (email, webhook, custom adapters).
/// - Composite default itera todos los canales con try-catch por
///   canal — un canal roto no afecta a los demás.
///
/// Llamado desde el background scanner POR cada cart detectado.
/// Excepciones del adapter se loguean pero NO interrumpen el scan
/// (siguiente cart sigue procesándose).
/// </remarks>
public interface ICartAbandonmentNotifier
{
    /// <summary>
    /// Notifica que un cart fue detectado como abandonado en el último
    /// scan. Llamado fire-and-forget por el background hosted service.
    /// </summary>
    Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken cancellationToken);
}

/// <summary>
/// Marker interface para canales individuales que componen un
/// <see cref="ICartAbandonmentNotifier"/> agregado.
/// </summary>
public interface ICartAbandonmentNotifierChannel : ICartAbandonmentNotifier
{
}
