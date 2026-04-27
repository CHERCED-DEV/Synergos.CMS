using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Discord-shaped para <see cref="ICartAbandonmentNotifierChannel"/>.
/// </summary>
public sealed class DiscordCartAbandonmentNotifier : ICartAbandonmentNotifierChannel
{
    private const string HttpClientName = "cart-abandonment-discord";
    private const int WarningAmberDecimal = 14_251_782; // 0xd97706

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CartAbandonmentSettings> _settings;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<DiscordCartAbandonmentNotifier> _logger;

    public DiscordCartAbandonmentNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CartAbandonmentSettings> settings,
        IBrandingProvider branding,
        ILogger<DiscordCartAbandonmentNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var minutesSinceActivity = (int)(DateTime.UtcNow - cart.LastActivityUtc).TotalMinutes;

        var payload = new
        {
            username = $"Synergos · {siteName}",
            embeds = new[]
            {
                new
                {
                    title = $"🛒 Carrito abandonado · {cart.Subtotal:N2} {cart.Currency}",
                    description = $"Considera recovery email o retargeting.",
                    color = WarningAmberDecimal,
                    timestamp = cart.LastActivityUtc.ToString("O"),
                    fields = new object[]
                    {
                        new { name = "Subtotal", value = $"{cart.Subtotal:N2} {cart.Currency}", inline = true },
                        new { name = "Items", value = cart.ItemCount.ToString(), inline = true },
                        new { name = "Inactivo", value = $"{minutesSinceActivity} min", inline = true },
                        new { name = "Cart ID", value = $"`{cart.CartId}`", inline = false },
                    },
                    footer = new { text = $"{siteName} · Scanner abandonment" },
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
                "Discord webhook returned non-success: status={Status} url={Url} cartId={CartId}",
                (int)response.StatusCode, settings.DiscordWebhookUrl, cart.CartId);
        }
    }
}
