using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal email para <see cref="IFormSubmissionNotifierChannel"/>.
/// Renderiza <c>Views/Emails/FormNotification.cshtml</c> via
/// <see cref="IEmailTemplateRenderer"/> y envía a
/// <see cref="FormsSettings.NotifyEmailAddress"/> via
/// <see cref="IEmailService"/>.
/// </summary>
/// <remarks>
/// Si <c>NotifyEmailAddress</c> está vacío, el canal es no-op.
/// Subject brand-aware via <see cref="IBrandingProvider"/>.
///
/// Reemplaza la lógica inline previa de
/// <c>FormSubmissionsController.SendNotificationAsync</c> (Olas
/// 80.3 + 82 + 85). El controller ahora solo conoce
/// <see cref="IFormSubmissionNotifier"/>.
/// </remarks>
public sealed class EmailFormSubmissionNotifier : IFormSubmissionNotifierChannel
{
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _emailRenderer;
    private readonly IBrandingProvider _branding;
    private readonly IOptions<FormsSettings> _options;
    private readonly ILogger<EmailFormSubmissionNotifier> _logger;

    public EmailFormSubmissionNotifier(
        IEmailService emailService,
        IEmailTemplateRenderer emailRenderer,
        IBrandingProvider branding,
        IOptions<FormsSettings> options,
        ILogger<EmailFormSubmissionNotifier> logger)
    {
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _branding = branding;
        _options = options;
        _logger = logger;
    }

    public async Task NotifySubmittedAsync(
        FormSubmissionRequest request,
        FormSubmissionResult result,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.NotifyEmailAddress))
        {
            return;
        }
        if (!result.Success)
        {
            return;
        }

        try
        {
            var brand = _branding.GetCurrent();
            var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

            var bodyHtml = await _emailRenderer.RenderAsync(
                viewName: "FormNotification",
                model: new FormNotificationEmailModel(
                    FormKey: request.FormKey,
                    Fields: request.Fields,
                    ClientIp: request.ClientIp ?? "unknown",
                    Referrer: request.Referrer ?? "/",
                    ReceivedAtUtc: request.ReceivedAtUtc,
                    SiteName: siteName),
                cancellationToken);

            await _emailService.SendAsync(new EmailMessage(
                To: settings.NotifyEmailAddress,
                Subject: $"{siteName} · Nueva submission del form: {request.FormKey}",
                BodyHtml: bodyHtml),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Form notification email failed: formKey={FormKey} to={To}",
                request.FormKey, settings.NotifyEmailAddress);
        }
    }
}
