using System.Net.Http.Headers;
using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Helper compartido para enviar mensajes Slack Block Kit a un
/// incoming webhook. Reusable por los 3 canales Slack
/// (Comments / Forms / Cart) — payload shape específico vive en
/// cada notifier; el POST + serialization + error logging vive aquí.
/// </summary>
/// <remarks>
/// Slack incoming webhooks NO requieren Authorization header — la URL
/// misma contiene el secret/token. Tampoco aceptan HMAC validation
/// inbound (Slack solo provee signing en outbound desde Slack a tu
/// app, no al revés).
///
/// Convención de uso por canal:
/// <code>
/// var payload = new {
///     text = "Fallback plain-text",
///     blocks = new[] { ... }   // Block Kit specs
/// };
/// await SlackWebhookSender.SendAsync(httpClient, slackUrl, payload, logger, contextLog, ct);
/// </code>
/// </remarks>
public static class SlackWebhookSender
{
    public static async Task SendAsync<T>(
        HttpClient httpClient,
        string webhookUrl,
        T payload,
        ILogger logger,
        string contextLog,
        CancellationToken cancellationToken)
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = content,
        };

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Slack webhook returned non-success: status={Status} url={Url} context={Context}",
                (int)response.StatusCode, webhookUrl, contextLog);
        }
    }
}
