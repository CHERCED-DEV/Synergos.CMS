using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal de email de las notificaciones transaccionales (T4, doc 25) — el único canal de
/// la Ola A. Renderiza UNA plantilla data-driven con el copy del tipo y envía vía
/// <see cref="IEmailService"/>. Molde: <see cref="EmailCartAbandonmentNotifier"/>.
/// </summary>
/// <remarks>
/// SMS/push serían otro <c>AddSingleton&lt;ITransactionalNotifierChannel, X&gt;()</c>, sin
/// tocar motores ni dispatcher. No se clonan los 5 transportes de las familias de ops
/// (Slack/Discord/Teams/Webhook): nadie pidió un recibo por Discord.
/// </remarks>
public sealed class EmailTransactionalNotifier : ITransactionalNotifierChannel
{
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _emailRenderer;
    private readonly IBrandingProvider _branding;
    private readonly IOptionsMonitor<NotificationsSettings> _settings;
    private readonly ILogger<EmailTransactionalNotifier> _logger;

    public EmailTransactionalNotifier(
        IEmailService emailService,
        IEmailTemplateRenderer emailRenderer,
        IBrandingProvider branding,
        IOptionsMonitor<NotificationsSettings> settings,
        ILogger<EmailTransactionalNotifier> logger)
    {
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _branding = branding;
        _settings = settings;
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;
        var copy = TransactionalEmailCopy.For(notification.Type, out var isFallback);
        if (isFallback)
        {
            _logger.LogWarning(
                "Notificación {Type} sin copy dedicado — se usa la plantilla genérica. Agregar el tipo a TransactionalEmailCopy.",
                notification.Type);
        }

        // El brand se resuelve AQUÍ y ahora: el canal corre dentro del request que confirmó
        // (el dispatcher lo invoca inline), así que IBrandingProvider ve el siteRoot vivo.
        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

        // Enlace absoluto solo si hay base configurada — nunca un href roto.
        string? actionUrl = null;
        if (!string.IsNullOrWhiteSpace(settings.PublicBaseUrl) && !string.IsNullOrWhiteSpace(notification.ActionPath))
        {
            actionUrl = settings.PublicBaseUrl!.TrimEnd('/') + "/" + notification.ActionPath!.TrimStart('/');
        }

        var lines = (notification.Lines ?? Array.Empty<NotificationLine>())
            .Select(l => new TransactionalEmailLine(l.Label, l.Quantity, l.Amount, l.Currency, l.Detail))
            .ToList();
        var rows = (notification.Data ?? new Dictionary<string, string>())
            .Select(kv => new TransactionalEmailRow(kv.Key, kv.Value))
            .ToList();

        var bodyHtml = await _emailRenderer.RenderAsync(
            viewName: "Transactional",
            model: new TransactionalEmailModel(
                SiteName: siteName,
                Title: copy.Subject,
                Preheader: $"{copy.CodeLabel}: {notification.Code}",
                Heading: copy.Heading,
                Intro: copy.Intro,
                ToName: notification.ToName,
                Code: notification.Code,
                CodeLabel: copy.CodeLabel,
                Lines: lines,
                Rows: rows,
                Total: notification.Amount,
                Currency: notification.Currency,
                ActionUrl: actionUrl,
                ActionLabel: actionUrl is null ? null : copy.ActionLabel),
            cancellationToken);

        await _emailService.SendAsync(
            new EmailMessage(
                To: notification.ToEmail,
                Subject: $"{siteName} · {copy.Subject} ({notification.Code})",
                BodyHtml: bodyHtml,
                ReplyTo: string.IsNullOrWhiteSpace(settings.ReplyTo) ? null : settings.ReplyTo),
            cancellationToken);
    }
}
