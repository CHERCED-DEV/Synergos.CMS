namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de notificación al equipo operador cuando un formulario fue
/// procesado correctamente por <see cref="IFormSubmissionHandler"/>.
/// Permite que el sitio adopte cualquier canal (email, Slack, webhook,
/// queue) sin acoplar al <c>FormSubmissionsController</c>.
/// </summary>
/// <remarks>
/// Pareja conceptual de <see cref="ICommentModerationNotifier"/>. Se
/// usa el mismo pattern Composite + Channel:
///
/// - <see cref="IFormSubmissionNotifier"/> es la fachada que el
///   controller inyecta.
/// - El composite default
///   (<c>CompositeFormSubmissionNotifier</c>) itera todos los
///   <see cref="IFormSubmissionNotifierChannel"/> registrados con
///   try-catch por canal.
///
/// Los datos en <see cref="FormSubmissionRequest"/> ya están filtrados
/// por el controller (sin honeypot, valores trimmed, longitud capped).
/// </remarks>
public interface IFormSubmissionNotifier
{
    /// <summary>
    /// Notifica que una submission fue procesada y persistida. Llamado
    /// desde el controller después de <c>IFormSubmissionHandler.SubmitAsync</c>.
    /// Excepciones del adapter NO deben romper el flow del visitante.
    /// </summary>
    Task NotifySubmittedAsync(
        FormSubmissionRequest request,
        FormSubmissionResult result,
        CancellationToken cancellationToken);
}

/// <summary>
/// Marker interface para canales individuales (email, webhook, Slack,
/// queue) que componen un <see cref="IFormSubmissionNotifier"/>
/// agregado. El composite default los acumula vía IEnumerable.
/// </summary>
public interface IFormSubmissionNotifierChannel : IFormSubmissionNotifier
{
}
