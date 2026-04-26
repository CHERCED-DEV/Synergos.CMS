using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal HTTP webhook para <see cref="ICommentModerationNotifierChannel"/>.
/// POST a <see cref="CommentsSettings.WebhookUrl"/> con payload JSON
/// del comentario pendiente. Compatible con Slack/Discord/Teams
/// incoming webhooks o cualquier endpoint custom HTTP.
/// </summary>
/// <remarks>
/// Si <c>WebhookUrl</c> está vacío, el canal es no-op. Errores HTTP
/// loguean Warning pero NO se re-throwean (handle por el composite).
///
/// Payload shape — flat JSON estable: documentar en
/// <c>docs/webhooks/comment-moderation.md</c> para integradores.
/// Para Slack-shaped messages (text + attachments), swap por adapter
/// SlackCommentModerationNotifier que mapee a <c>{"text": "..."}</c>.
/// </remarks>
public sealed class WebhookCommentModerationNotifier : ICommentModerationNotifierChannel
{
    private const string HttpClientName = "comment-moderation-webhook";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<CommentsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<WebhookCommentModerationNotifier> _logger;

    public WebhookCommentModerationNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<CommentsSettings> options,
        IBrandingProvider branding,
        ILogger<WebhookCommentModerationNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _branding = branding;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifyPendingAsync(Comment comment, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

        var payload = new
        {
            @event = "comment.pending-moderation",
            siteName,
            nodeId = comment.NodeId,
            commentId = comment.Id,
            authorName = comment.AuthorName,
            body = comment.Body,
            createdAtUtc = comment.CreatedAtUtc.ToString("O"),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(settings.WebhookBearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.WebhookBearerToken);
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Comment webhook returned non-success: status={Status} url={Url} nodeId={NodeId}",
                (int)response.StatusCode, settings.WebhookUrl, comment.NodeId);
        }
    }
}
