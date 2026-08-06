using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Microsoft Teams-shaped para
/// <see cref="ICommentModerationNotifierChannel"/>. POST a
/// <see cref="CommentsSettings.TeamsWebhookUrl"/> con payload formato
/// O365 MessageCard (sections + facts + activityTitle).
/// </summary>
/// <remarks>
/// MessageCard sigue siendo soportado por incoming webhooks de Teams
/// (URLs <c>/webhookb2/...</c>) aunque Microsoft empuje Adaptive
/// Cards. Para Adaptive Cards el shape es muy distinto y requeriría
/// otro adapter aparte. Diferido.
/// </remarks>
public sealed class TeamsCommentModerationNotifier : ICommentModerationNotifierChannel
{
    private const string HttpClientName = "comment-moderation-teams";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<CommentsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<TeamsCommentModerationNotifier> _logger;

    public TeamsCommentModerationNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<CommentsSettings> options,
        IBrandingProvider branding,
        ILogger<TeamsCommentModerationNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var truncated = comment.Body.Length > 800 ? comment.Body[..797] + "..." : comment.Body;

        // Ola 128 — Adaptive Cards reemplaza MessageCard.
        // Formato: { type:"message", attachments:[{ contentType:adaptive,
        // content:{ AdaptiveCard schema 1.4 } }] }
        var adaptiveCard = new
        {
            type = "AdaptiveCard",
            schema = "http://adaptivecards.io/schemas/adaptive-card.json",
            version = "1.4",
            body = new object[]
            {
                new
                {
                    type = "TextBlock",
                    text = $"💬 Comentario pendiente · {siteName}",
                    weight = "Bolder",
                    size = "Medium",
                    wrap = true,
                },
                new
                {
                    type = "TextBlock",
                    text = $"De **{comment.AuthorName}** en nodo #{comment.NodeId}",
                    isSubtle = true,
                    spacing = "None",
                    wrap = true,
                },
                new
                {
                    type = "TextBlock",
                    text = truncated,
                    wrap = true,
                    spacing = "Medium",
                },
                new
                {
                    type = "FactSet",
                    facts = new object[]
                    {
                        new { title = "ID", value = comment.Id },
                        new { title = "Recibido", value = comment.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm") + " UTC" },
                    },
                    spacing = "Medium",
                },
            },
        };
        var payload = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = adaptiveCard,
                },
            },
        };

        await PostMessageCardAsync(payload, settings.TeamsWebhookUrl,
            $"comment.pending nodeId={comment.NodeId} commentId={comment.Id}",
            cancellationToken);
    }

    private async Task PostMessageCardAsync(
        object payload,
        string webhookUrl,
        string contextLog,
        CancellationToken cancellationToken)
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.PostAsync(webhookUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Teams webhook returned non-success: status={Status} url={Url} context={Context}",
                (int)response.StatusCode, webhookUrl, contextLog);
        }
    }
}
