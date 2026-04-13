namespace Synergos.CMS.Domain.Orchestration;

/// <summary>
/// Contrato para el dispatcher de webhooks del Flow Engine.
///
/// Separa el contrato de la implementación (que usa IHttpClientFactory + HMAC).
/// Esto permite que FlowOrchestrationController sea testeable sin HTTP real.
///
/// Patrón de referencia: NS.Booking.CMS — Proxy Pattern con interfaces limpias.
/// </summary>
public interface IFlowWebhookDispatcher
{
    /// <summary>
    /// Serializa <paramref name="flow"/>, firma con HMAC-SHA256 y hace POST a su WebhookTargetUrl.
    /// No lanza excepciones — los errores se reportan en el resultado.
    /// </summary>
    Task<FlowPublicationResult> PublishAsync(FlowConfig flow, CancellationToken ct = default);

    /// <summary>
    /// Releva una solicitud de ejecución al engine externo.
    /// Devuelve (Success, ResponseBody, HttpStatusCode).
    /// </summary>
    Task<(bool Success, string? Body, int? StatusCode)> TriggerAsync(
        FlowConfig                  flow,
        string                      caseId,
        Dictionary<string, object?> input,
        CancellationToken           ct = default);
}
