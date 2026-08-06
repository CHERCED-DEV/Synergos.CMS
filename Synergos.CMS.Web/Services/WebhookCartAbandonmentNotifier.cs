using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal HTTP webhook para <see cref="ICartAbandonmentNotifierChannel"/>.
/// POST a <see cref="CartAbandonmentSettings.WebhookUrl"/> con payload
/// JSON. Compatible con Slack/Discord/Teams incoming webhooks o
/// cualquier endpoint custom HTTP. HMAC-SHA256 signature opcional via
/// <see cref="WebhookSigner"/>.
/// </summary>
public sealed class WebhookCartAbandonmentNotifier : ICartAbandonmentNotifierChannel
{
    private const string HttpClientName = "cart-abandonment-webhook";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CartAbandonmentSettings> _settings;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<WebhookCartAbandonmentNotifier> _logger;

    public WebhookCartAbandonmentNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CartAbandonmentSettings> settings,
        IBrandingProvider branding,
        ILogger<WebhookCartAbandonmentNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var minutesSinceActivity = (int)(DateTime.UtcNow - cart.LastActivityUtc).TotalMinutes;

        var payload = new
        {
            @event = "cart.abandoned",
            siteName,
            cartId = cart.CartId,
            itemCount = cart.ItemCount,
            subtotal = cart.Subtotal,
            currency = cart.Currency,
            lastActivityUtc = cart.LastActivityUtc.ToString("O"),
            minutesSinceActivity,
        };

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
        {
            Content = content,
        };

        if (!string.IsNullOrWhiteSpace(settings.WebhookBearerToken))
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.WebhookBearerToken);
        }

        var signed = WebhookSigner.ComputeSignedHeaders(settings.WebhookHmacSecret, bodyBytes);
        if (signed is { } s)
        {
            requestMessage.Headers.TryAddWithoutValidation(WebhookSigner.TimestampHeaderName, s.Timestamp);
            requestMessage.Headers.TryAddWithoutValidation(WebhookSigner.SignatureHeaderName, s.Signature);
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cart abandonment webhook returned non-success: status={Status} url={Url} cartId={CartId}",
                (int)response.StatusCode, settings.WebhookUrl, cart.CartId);
        }
    }
}
