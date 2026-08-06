using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Microsoft Teams-shaped para <see cref="IAlertNotifierChannel"/>.
/// Adaptive Card 1.4 con color "Attention" para alert, "Good" para
/// recovery. Cap-260 Batch B (Ola 255).
/// </summary>
public sealed class TeamsAlertNotifier : IAlertNotifierChannel
{
    private const string HttpClientName = "alert-teams";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<TeamsAlertNotifier> _logger;

    public TeamsAlertNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<TeamsAlertNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.TeamsWebhookUrl)) return;

        var card = new
        {
            type = "AdaptiveCard",
            schema = "http://adaptivecards.io/schemas/adaptive-card.json",
            version = "1.4",
            body = new object[]
            {
                new { type = "TextBlock", text = $"🚨 Channel breach · {alert.ChannelName}",
                    weight = "Bolder", size = "Medium", color = "Attention", wrap = true },
                new { type = "TextBlock",
                    text = $"Fail rate {alert.FailRate:P1} cruzó threshold {alert.Threshold:P1}.",
                    isSubtle = true, spacing = "None", wrap = true },
                new
                {
                    type = "FactSet",
                    facts = new object[]
                    {
                        new { title = "Total calls", value = alert.TotalCalls.ToString() },
                        new { title = "Failures", value = alert.FailureCount.ToString() },
                        new { title = "P50 / P95 / P99 ms", value = $"{alert.P50LatencyMs:F0} / {alert.P95LatencyMs:F0} / {alert.P99LatencyMs:F0}" },
                        new { title = "Cooldown", value = $"{alert.CooldownMinutes} min" },
                    },
                    spacing = "Medium",
                },
            },
        };

        await PostAdaptiveCardAsync(s.TeamsWebhookUrl, card, $"alert.fired channel={alert.ChannelName}", cancellationToken);
    }

    public async Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.TeamsWebhookUrl)) return;

        var card = new
        {
            type = "AdaptiveCard",
            schema = "http://adaptivecards.io/schemas/adaptive-card.json",
            version = "1.4",
            body = new object[]
            {
                new { type = "TextBlock", text = $"✅ Channel recovered · {recovery.ChannelName}",
                    weight = "Bolder", size = "Medium", color = "Good", wrap = true },
                new { type = "TextBlock",
                    text = $"Fail rate volvió a {recovery.CurrentFailRate:P1} (threshold {recovery.Threshold:P1}).",
                    isSubtle = true, spacing = "None", wrap = true },
                new
                {
                    type = "FactSet",
                    facts = new object[]
                    {
                        new { title = "Fail rate al disparar", value = $"{recovery.PriorFailRate:P1}" },
                        new { title = "Duración alerting", value = $"{recovery.AlertingDuration.TotalMinutes:F0} min" },
                        new { title = "Total calls", value = $"{recovery.TotalCalls} ({recovery.SuccessCount} OK / {recovery.FailureCount} fail)" },
                        new { title = "First fired UTC", value = recovery.FirstFiredUtc.ToString("yyyy-MM-dd HH:mm") },
                    },
                    spacing = "Medium",
                },
            },
        };

        await PostAdaptiveCardAsync(s.TeamsWebhookUrl, card, $"alert.recovered channel={recovery.ChannelName}", cancellationToken);
    }

    private async Task PostAdaptiveCardAsync(string url, object card, string contextLog, CancellationToken cancellationToken)
    {
        var payload = new
        {
            type = "message",
            attachments = new[]
            {
                new { contentType = "application/vnd.microsoft.card.adaptive", content = card },
            },
        };

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Teams alert webhook returned non-success: status={Status} context={Context}",
                (int)response.StatusCode, contextLog);
        }
    }
}
