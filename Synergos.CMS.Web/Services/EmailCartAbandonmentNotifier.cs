using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal email para <see cref="ICartAbandonmentNotifierChannel"/>.
/// Renderiza <c>Views/Emails/CartAbandonment.cshtml</c> via
/// <see cref="IEmailTemplateRenderer"/> y envía a
/// <see cref="CartAbandonmentSettings.NotifyEmailAddress"/>.
/// </summary>
/// <remarks>
/// Si <c>NotifyEmailAddress</c> está vacío el canal es no-op. Subject
/// brand-aware via <see cref="IBrandingProvider"/>.
///
/// Llamado desde un <c>BackgroundService</c> sin HttpContext del
/// request original — por eso el SiteName se resuelve via el brand
/// activo (que el scanner conoce porque se inyecta singleton).
/// </remarks>
public sealed class EmailCartAbandonmentNotifier : ICartAbandonmentNotifierChannel
{
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _emailRenderer;
    private readonly IBrandingProvider _branding;
    private readonly IOptionsMonitor<CartAbandonmentSettings> _settings;
    private readonly ILogger<EmailCartAbandonmentNotifier> _logger;

    public EmailCartAbandonmentNotifier(
        IEmailService emailService,
        IEmailTemplateRenderer emailRenderer,
        IBrandingProvider branding,
        IOptionsMonitor<CartAbandonmentSettings> settings,
        ILogger<EmailCartAbandonmentNotifier> logger)
    {
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _branding = branding;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(settings.NotifyEmailAddress))
        {
            return;
        }

        try
        {
            var brand = _branding.GetCurrent();
            var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
            var minutesSinceActivity = (int)(DateTime.UtcNow - cart.LastActivityUtc).TotalMinutes;

            var bodyHtml = await _emailRenderer.RenderAsync(
                viewName: "CartAbandonment",
                model: new CartAbandonmentEmailModel(
                    CartId: cart.CartId,
                    ItemCount: cart.ItemCount,
                    Subtotal: cart.Subtotal,
                    Currency: cart.Currency,
                    LastActivityUtc: cart.LastActivityUtc,
                    MinutesSinceActivity: minutesSinceActivity,
                    SiteName: siteName),
                cancellationToken);

            await _emailService.SendAsync(new EmailMessage(
                To: settings.NotifyEmailAddress,
                Subject: $"{siteName} · Carrito abandonado: {cart.Subtotal:N2} {cart.Currency}",
                BodyHtml: bodyHtml),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cart abandonment email notification failed: cartId={CartId} to={To}",
                cart.CartId, settings.NotifyEmailAddress);
        }
    }
}
