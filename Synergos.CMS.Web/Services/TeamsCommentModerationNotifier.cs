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
    private const string BrandIndigoHex = "4f6ef7";

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

        var payload = new Dictionary<string, object?>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["summary"] = $"Comentario pendiente · {siteName}",
            ["themeColor"] = BrandIndigoHex,
            ["title"] = $"💬 Comentario pendiente · {siteName}",
            ["sections"] = new object[]
            {
                new
                {
                    activityTitle = comment.AuthorName,
                    activitySubtitle = $"Nodo #{comment.NodeId} · {comment.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC",
                    text = truncated,
                    facts = new object[]
                    {
                        new { name = "ID", value = comment.Id },
                        new { name = "Recibido", value = comment.CreatedAtUtc.ToString("O") },
                    },
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
