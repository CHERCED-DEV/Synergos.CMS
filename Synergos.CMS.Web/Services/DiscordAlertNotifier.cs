using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Discord-shaped para <see cref="IAlertNotifierChannel"/>. Embed
/// con color rojo para alert, verde para recovery. Cap-260 Batch B
/// (Ola 255).
/// </summary>
public sealed class DiscordAlertNotifier : IAlertNotifierChannel
{
    private const string HttpClientName = "alert-discord";
    private const int DangerRedDecimal = 14_500_106; // 0xdc2626
    private const int SuccessGreenDecimal = 2_273_589; // 0x22c55e — recovery

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<DiscordAlertNotifier> _logger;

    public DiscordAlertNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<DiscordAlertNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.DiscordWebhookUrl)) return;

        var payload = new
        {
            username = "Synergos Telemetry",
            embeds = new[]
            {
                new
                {
                    title = $"🚨 Channel breach · {alert.ChannelName}",
                    description = $"Fail rate cruzó threshold. Cooldown {alert.CooldownMinutes} min antes de re-alertar.",
                    color = DangerRedDecimal,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    fields = new object[]
                    {
                        new { name = "Fail rate", value = $"{alert.FailRate:P1} (threshold {alert.Threshold:P1})", inline = true },
                        new { name = "Total calls", value = alert.TotalCalls.ToString(), inline = true },
                        new { name = "Failures", value = alert.FailureCount.ToString(), inline = true },
                        new { name = "P50 / P95 / P99", value = $"{alert.P50LatencyMs:F0} / {alert.P95LatencyMs:F0} / {alert.P99LatencyMs:F0} ms", inline = false },
                    },
                },
            },
        };

        await PostJsonAsync(s.DiscordWebhookUrl, payload, $"alert.fired channel={alert.ChannelName}", cancellationToken);
    }

    public async Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.DiscordWebhookUrl)) return;

        var payload = new
        {
            username = "Synergos Telemetry",
            embeds = new[]
            {
                new
                {
                    title = $"✅ Channel recovered · {recovery.ChannelName}",
                    description = $"Fail rate volvió bajo threshold. Duración del incidente: {recovery.AlertingDuration.TotalMinutes:F0} min.",
                    color = SuccessGreenDecimal,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    fields = new object[]
                    {
                        new { name = "Fail rate ahora", value = $"{recovery.CurrentFailRate:P1}", inline = true },
                        new { name = "Fail rate al disparar", value = $"{recovery.PriorFailRate:P1}", inline = true },
                        new { name = "Threshold", value = $"{recovery.Threshold:P1}", inline = true },
                        new { name = "Total calls", value = $"{recovery.TotalCalls} ({recovery.SuccessCount} OK / {recovery.FailureCount} fail)", inline = false },
                    },
                },
            },
        };

        await PostJsonAsync(s.DiscordWebhookUrl, payload, $"alert.recovered channel={recovery.ChannelName}", cancellationToken);
    }

    private async Task PostJsonAsync<T>(string url, T payload, string contextLog, CancellationToken cancellationToken)
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Discord alert webhook returned non-success: status={Status} context={Context}",
                (int)response.StatusCode, contextLog);
        }
    }
}
