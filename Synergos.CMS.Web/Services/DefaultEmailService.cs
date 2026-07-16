using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using UmbracoMail = Umbraco.Cms.Core.Mail;
using UmbracoEmail = Umbraco.Cms.Core.Models.Email;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IEmailService"/>. Adapter sobre el
/// <see cref="UmbracoMail.IEmailSender"/> de Umbraco — Umbraco gestiona
/// la config SMTP (sección <c>Umbraco:CMS:Global:Smtp</c>) y el
/// pickup directory para dev. Este wrapper provee DTOs estables y
/// behavior de logging consistente con el resto de seams Synergos.
/// </summary>
/// <remarks>
/// Si <c>WarnOnMissingSmtp = true</c> y el envío falla por
/// configuración ausente, logea Warning explícito (más útil que el
/// Error opaco que loguea Umbraco internamente).
///
/// <c>isTransactional: true</c> en todos los envíos — el seam está
/// pensado para password reset, registration confirmation, form
/// notifications. Para newsletters/marketing con quoting de unsubscribe,
/// agregar un seam separado <c>IBulkEmailService</c> con su propio
/// adapter (SendGrid Marketing API, Mailchimp).
/// </remarks>
public sealed class DefaultEmailService : IEmailService
{
    private readonly UmbracoMail.IEmailSender _umbracoEmailSender;
    private readonly IOptions<EmailSettings> _options;
    private readonly ILogger<DefaultEmailService> _logger;

    public DefaultEmailService(
        UmbracoMail.IEmailSender umbracoEmailSender,
        IOptions<EmailSettings> options,
        ILogger<DefaultEmailService> logger)
    {
        _umbracoEmailSender = umbracoEmailSender;
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var fromHeader = string.IsNullOrWhiteSpace(message.From)
            ? $"{settings.FromName} <{settings.FromAddress}>"
            : message.From;

        var umbracoMessage = new UmbracoEmail.EmailMessage(
            from: fromHeader,
            to: message.To,
            subject: message.Subject,
            body: message.BodyHtml,
            isBodyHtml: true);

        // VERACIDAD (T4): sin transporte configurado, el IEmailSender de Umbraco hace
        // LogDebug + return SILENCIOSO — no lanza. Sin este pre-check el catch de abajo
        // jamás corre y se loguea "Email sent" afirmando un envío QUE NUNCA OCURRIÓ: un
        // grep de los logs daría por buena una entrega inexistente. El log debe decir la
        // verdad o no sirve para verificar nada.
        if (!_umbracoEmailSender.CanSendRequiredEmail())
        {
            if (settings.WarnOnMissingSmtp)
            {
                _logger.LogWarning(
                    "Email NO enviado — no hay transporte configurado: to={To} subject={Subject}. " +
                    "Configura Umbraco:CMS:Global:Smtp (Host, o DeliveryMethod=SpecifiedPickupDirectory " +
                    "+ PickupDirectoryLocation para escribir .eml a disco sin SMTP).",
                    message.To,
                    message.Subject);
            }
            return;
        }

        try
        {
            await _umbracoEmailSender.SendAsync(umbracoMessage, "synergos");
            _logger.LogInformation(
                "Email sent: to={To} subject={Subject}",
                message.To,
                message.Subject);
        }
        catch (Exception ex)
        {
            if (settings.WarnOnMissingSmtp)
            {
                _logger.LogWarning(ex,
                    "Email send failed (verifica Umbraco:CMS:Global:Smtp en appsettings o pickup directory): " +
                    "to={To} subject={Subject}",
                    message.To,
                    message.Subject);
            }
        }
    }
}
