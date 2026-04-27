using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Microsoft Teams-shaped para
/// <see cref="ICartAbandonmentNotifierChannel"/>.
/// </summary>
public sealed class TeamsCartAbandonmentNotifier : ICartAbandonmentNotifierChannel
{
    private const string HttpClientName = "cart-abandonment-teams";
    private const string WarningAmberHex = "d97706";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CartAbandonmentSettings> _settings;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<TeamsCartAbandonmentNotifier> _logger;

    public TeamsCartAbandonmentNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CartAbandonmentSettings> settings,
        IBrandingProvider branding,
        ILogger<TeamsCartAbandonmentNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var minutesSinceActivity = (int)(DateTime.UtcNow - cart.LastActivityUtc).TotalMinutes;

        var payload = new Dictionary<string, object?>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["summary"] = $"Carrito abandonado · {cart.Subtotal:N2} {cart.Currency}",
            ["themeColor"] = WarningAmberHex,
            ["title"] = $"🛒 Carrito abandonado · {siteName}",
            ["sections"] = new object[]
            {
                new
                {
                    activityTitle = $"{cart.Subtotal:N2} {cart.Currency}",
                    activitySubtitle = $"Inactivo hace {minutesSinceActivity} min · {cart.LastActivityUtc:yyyy-MM-dd HH:mm} UTC",
                    text = "Considera enviar un recovery email o retargeting para recuperar la conversión.",
                    facts = new object[]
                    {
                        new { name = "Subtotal", value = $"{cart.Subtotal:N2} {cart.Currency}" },
                        new { name = "Items", value = cart.ItemCount.ToString() },
                        new { name = "Inactivo (min)", value = minutesSinceActivity.ToString() },
                        new { name = "Cart ID", value = cart.CartId },
                    },
                },
            },
        };

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.PostAsync(settings.TeamsWebhookUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Teams webhook returned non-success: status={Status} url={Url} cartId={CartId}",
                (int)response.StatusCode, settings.TeamsWebhookUrl, cart.CartId);
        }
    }
}
