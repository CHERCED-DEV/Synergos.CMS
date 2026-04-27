using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Slack-shaped para <see cref="ICartAbandonmentNotifierChannel"/>.
/// POST a <see cref="CartAbandonmentSettings.SlackWebhookUrl"/> con
/// payload Block Kit.
/// </summary>
public sealed class SlackCartAbandonmentNotifier : ICartAbandonmentNotifierChannel
{
    private const string HttpClientName = "cart-abandonment-slack";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CartAbandonmentSettings> _settings;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<SlackCartAbandonmentNotifier> _logger;

    public SlackCartAbandonmentNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CartAbandonmentSettings> settings,
        IBrandingProvider branding,
        ILogger<SlackCartAbandonmentNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _branding = branding;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var minutesSinceActivity = (int)(DateTime.UtcNow - cart.LastActivityUtc).TotalMinutes;

        var payload = new
        {
            text = $"[{siteName}] Carrito abandonado · {cart.Subtotal:N2} {cart.Currency}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = $"🛒 Carrito abandonado · {siteName}" },
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Subtotal:*\n{cart.Subtotal:N2} {cart.Currency}" },
                        new { type = "mrkdwn", text = $"*Items:*\n{cart.ItemCount}" },
                        new { type = "mrkdwn", text = $"*Inactivo:*\nhace {minutesSinceActivity} min" },
                        new { type = "mrkdwn", text = $"*Cart ID:*\n`{cart.CartId}`" },
                    },
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new { type = "mrkdwn", text = $"_Última actividad: {cart.LastActivityUtc:yyyy-MM-dd HH:mm} UTC. Considera enviar un recovery email o retargeting._" },
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
            $"cart.abandoned cartId={cart.CartId}");
    }
}
