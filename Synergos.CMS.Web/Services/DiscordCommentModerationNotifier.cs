using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Discord-shaped para <see cref="ICommentModerationNotifierChannel"/>.
/// POST a <see cref="CommentsSettings.DiscordWebhookUrl"/> con payload
/// formato Discord embeds (color brand indigo + fields inline + timestamp).
/// </summary>
public sealed class DiscordCommentModerationNotifier : ICommentModerationNotifierChannel
{
    private const string HttpClientName = "comment-moderation-discord";
    private const int BrandIndigoDecimal = 5_205_751;       // 0x4f6ef7

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<CommentsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<DiscordCommentModerationNotifier> _logger;

    public DiscordCommentModerationNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<CommentsSettings> options,
        IBrandingProvider branding,
        ILogger<DiscordCommentModerationNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var truncated = comment.Body.Length > 1500 ? comment.Body[..1497] + "..." : comment.Body;

        var payload = new
        {
            username = $"Synergos · {siteName}",
            embeds = new[]
            {
                new
                {
                    title = "💬 Comentario pendiente",
                    description = truncated,
                    color = BrandIndigoDecimal,
                    timestamp = comment.CreatedAtUtc.ToString("O"),
                    fields = new object[]
                    {
                        new { name = "Autor", value = comment.AuthorName, inline = true },
                        new { name = "Nodo", value = $"#{comment.NodeId}", inline = true },
                        new { name = "ID", value = $"`{comment.Id}`", inline = false },
                    },
                    footer = new { text = $"{siteName} · Cola moderación" },
                },
            },
        };

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.PostAsync(settings.DiscordWebhookUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Discord webhook returned non-success: status={Status} url={Url} commentId={CommentId}",
                (int)response.StatusCode, settings.DiscordWebhookUrl, comment.Id);
        }
    }
}
