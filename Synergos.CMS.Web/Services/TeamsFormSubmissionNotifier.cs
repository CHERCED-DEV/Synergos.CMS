using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Microsoft Teams-shaped para
/// <see cref="IFormSubmissionNotifierChannel"/>.
/// </summary>
public sealed class TeamsFormSubmissionNotifier : IFormSubmissionNotifierChannel
{
    private const string HttpClientName = "form-submission-teams";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<FormsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<TeamsFormSubmissionNotifier> _logger;

    public TeamsFormSubmissionNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<FormsSettings> options,
        IBrandingProvider branding,
        ILogger<TeamsFormSubmissionNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl))
        {
            return;
        }
        if (!result.Success)
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

        // Ola 128 — Adaptive Cards. FactSet con primeros 12 fields.
        var facts = request.Fields
            .Take(12)
            .Select(kv => new
            {
                title = kv.Key,
                value = kv.Value.Length > 240 ? kv.Value[..237] + "..." : kv.Value,
            })
            .ToArray<object>();

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
                    text = $"📩 Form: {request.FormKey} · {siteName}",
                    weight = "Bolder",
                    size = "Medium",
                    wrap = true,
                },
                new
                {
                    type = "TextBlock",
                    text = $"IP: {request.ClientIp ?? "unknown"} · {request.Referrer ?? "/"} · {request.ReceivedAtUtc:yyyy-MM-dd HH:mm} UTC",
                    isSubtle = true,
                    spacing = "None",
                    wrap = true,
                },
                new
                {
                    type = "FactSet",
                    facts,
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
                "Teams webhook returned non-success: status={Status} url={Url} formKey={FormKey}",
                (int)response.StatusCode, settings.TeamsWebhookUrl, request.FormKey);
        }
    }
}
