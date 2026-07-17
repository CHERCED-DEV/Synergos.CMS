using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Discord-shaped para <see cref="IFormSubmissionNotifierChannel"/>.
/// </summary>
public sealed class DiscordFormSubmissionNotifier : IFormSubmissionNotifierChannel
{
    private const string HttpClientName = "form-submission-discord";
    private const int BrandIndigoDecimal = 5_205_751; // 0x4f6ef7

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<FormsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<DiscordFormSubmissionNotifier> _logger;

    public DiscordFormSubmissionNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<FormsSettings> options,
        IBrandingProvider branding,
        ILogger<DiscordFormSubmissionNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _branding = branding;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifySubmittedAsync(
        FormSubmissionRequest request,
        FormSubmissionResult result,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            return;
        }
        if (!result.Success)
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

        // Discord embed limit: 25 fields max, value max 1024 chars.
        var fields = request.Fields
            .Take(20)
            .Select(kv => new
            {
                name = Truncate(kv.Key, 256),
                value = Truncate(kv.Value, 1024),
                inline = kv.Value.Length < 60,
            })
            .ToArray<object>();

        var payload = new
        {
            username = $"Synergos · {siteName}",
            embeds = new[]
            {
                new
                {
                    title = $"📩 Form: {request.FormKey}",
                    description = $"Nueva submission desde `{request.Referrer ?? "/"}`",
                    color = BrandIndigoDecimal,
                    timestamp = request.ReceivedAtUtc.ToString("O"),
                    fields,
                    footer = new { text = $"{siteName} · IP {request.ClientIp ?? "unknown"}" },
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
                "Discord webhook returned non-success: status={Status} url={Url} formKey={FormKey}",
                (int)response.StatusCode, settings.DiscordWebhookUrl, request.FormKey);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 3)] + "...";
}
