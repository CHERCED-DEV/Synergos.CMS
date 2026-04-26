using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="ICommentModerationNotifier"/> que envía un email
/// al moderador definido en
/// <see cref="CommentsSettings.NotifyEmailAddress"/>. Si el address
/// está vacío, el notifier es no-op (no logea, no envía, no falla).
/// </summary>
/// <remarks>
/// Diseñado fire-and-forget: cualquier excepción del adapter de email
/// se loguea como Warning y NO se re-throwea — el visitante NO debe
/// ver fallos de SMTP.
///
/// Para sitios con multi-brand cada notificación lleva el SiteName
/// del request actual (resuelto via <see cref="IBrandingProvider"/>).
/// </remarks>
public sealed class EmailCommentModerationNotifier : ICommentModerationNotifier
{
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _emailRenderer;
    private readonly IBrandingProvider _branding;
    private readonly IOptions<CommentsSettings> _options;
    private readonly ILogger<EmailCommentModerationNotifier> _logger;

    public EmailCommentModerationNotifier(
        IEmailService emailService,
        IEmailTemplateRenderer emailRenderer,
        IBrandingProvider branding,
        IOptions<CommentsSettings> options,
        ILogger<EmailCommentModerationNotifier> logger)
    {
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _branding = branding;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyPendingAsync(Comment comment, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.NotifyEmailAddress))
        {
            return;
        }

        try
        {
            var brand = _branding.GetCurrent();
            var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

            // URL relativa porque el notifier no tiene HttpContext del
            // request original (puede correr fire-and-forget). El
            // moderator agrega el host manualmente desde su email
            // client. Para URL absoluta, swap por adapter que
            // resuelva el host del brand activo.
            var queueUrl = "/api/comments/moderation/pending";

            var bodyHtml = await _emailRenderer.RenderAsync(
                viewName: "CommentPendingModeration",
                model: new CommentPendingModerationEmailModel(
                    NodeId: comment.NodeId,
                    CommentId: comment.Id,
                    AuthorName: comment.AuthorName,
                    Body: comment.Body,
                    CreatedAtUtc: comment.CreatedAtUtc,
                    ModerationQueueUrl: queueUrl,
                    SiteName: siteName),
                cancellationToken);

            await _emailService.SendAsync(new EmailMessage(
                To: settings.NotifyEmailAddress,
                Subject: $"{siteName} · Comentario pendiente de moderación",
                BodyHtml: bodyHtml),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Comment moderation notification failed: nodeId={NodeId} commentId={CommentId}",
                comment.NodeId, comment.Id);
        }
    }
}
