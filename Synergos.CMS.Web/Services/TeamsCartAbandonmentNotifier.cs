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

        // Ola 128 — Adaptive Cards. Color implícito (warning) via host theme.
        var adaptiveCard = new
        {
            type = "AdaptiveCard",
            schema = "http://adaptivecards.io/schemas/adaptive-card.json",
            version = "1.4",
            body = new object[]
            {
                new
                {
                    type = "TextBlock",
                    text = $"🛒 Carrito abandonado · {siteName}",
                    weight = "Bolder",
                    size = "Medium",
                    color = "Warning",
                    wrap = true,
                },
                new
                {
                    type = "TextBlock",
                    text = $"{cart.Subtotal:N2} {cart.Currency} · Inactivo hace {minutesSinceActivity} min",
                    isSubtle = true,
                    spacing = "None",
                    wrap = true,
                },
                new
                {
                    type = "TextBlock",
                    text = "Considera enviar un recovery email o retargeting para recuperar la conversión.",
                    wrap = true,
                    spacing = "Medium",
                },
                new
                {
                    type = "FactSet",
                    facts = new object[]
                    {
                        new { title = "Subtotal", value = $"{cart.Subtotal:N2} {cart.Currency}" },
                        new { title = "Items", value = cart.ItemCount.ToString() },
                        new { title = "Inactivo (min)", value = minutesSinceActivity.ToString() },
                        new { title = "Cart ID", value = cart.CartId },
                    },
                    spacing = "Medium",
                },
            },
        };
        var payload = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = adaptiveCard,
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
