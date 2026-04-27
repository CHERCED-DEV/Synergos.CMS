using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Slack-shaped para <see cref="ICommentModerationNotifierChannel"/>.
/// POST a <see cref="CommentsSettings.SlackWebhookUrl"/> con payload
/// Block Kit (header + section fields + body + action button).
/// </summary>
/// <remarks>
/// Independiente del <c>WebhookCommentModerationNotifier</c> (que emite
/// flat JSON genérico). Un sitio puede tener ambos URLs configurados —
/// el composite los itera y dispara cada uno.
/// </remarks>
public sealed class SlackCommentModerationNotifier : ICommentModerationNotifierChannel
{
    private const string HttpClientName = "comment-moderation-slack";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<CommentsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<SlackCommentModerationNotifier> _logger;

    public SlackCommentModerationNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<CommentsSettings> options,
        IBrandingProvider branding,
        ILogger<SlackCommentModerationNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var truncatedBody = comment.Body.Length > 500
            ? comment.Body[..497] + "..."
            : comment.Body;

        var payload = new
        {
            text = $"[{siteName}] Comentario pendiente de moderación de {comment.AuthorName}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = $"💬 Comentario pendiente · {siteName}" },
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Autor:*\n{comment.AuthorName}" },
                        new { type = "mrkdwn", text = $"*Nodo:*\n#{comment.NodeId}" },
                        new { type = "mrkdwn", text = $"*Recibido:*\n{comment.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC" },
                        new { type = "mrkdwn", text = $"*ID:*\n`{comment.Id}`" },
                    },
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = $"*Contenido:*\n>{truncatedBody.Replace("\n", "\n>")}" },
                },
                new
                {
                    type = "actions",
                    elements = new object[]
                    {
                        new
                        {
                            type = "button",
                            text = new { type = "plain_text", text = "Abrir cola de moderación" },
                            url = "/api/comments/moderation/pending",
                            style = "primary",
                        },
                    },
                },
            },
        };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        await SlackWebhookSender.SendAsync(
            httpClient,
            settings.SlackWebhookUrl,
            payload,
            cancellationToken,
            _logger,
            $"comment.pending nodeId={comment.NodeId} commentId={comment.Id}");
    }
}
