using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal HTTP webhook custom para <see cref="IAlertNotifierChannel"/>.
/// POST raw JSON con event slug + payload completo. Soporta
/// Bearer auth + HMAC-SHA256 signature via <see cref="WebhookSigner"/>.
/// Cap-260 Batch B (Ola 255).
/// </summary>
public sealed class WebhookAlertNotifier : IAlertNotifierChannel
{
    private const string HttpClientName = "alert-webhook";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<WebhookAlertNotifier> _logger;

    public WebhookAlertNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<WebhookAlertNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.WebhookUrl)) return Task.CompletedTask;

        var payload = new
        {
            @event = "alert.fired",
            channelName = alert.ChannelName,
            failRate = alert.FailRate,
            threshold = alert.Threshold,
            totalCalls = alert.TotalCalls,
            successCount = alert.SuccessCount,
            failureCount = alert.FailureCount,
            p50LatencyMs = alert.P50LatencyMs,
            p95LatencyMs = alert.P95LatencyMs,
            p99LatencyMs = alert.P99LatencyMs,
            lastObservedUtc = alert.LastObservedUtc?.ToString("O"),
            cooldownMinutes = alert.CooldownMinutes,
            firedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        return PostJsonAsync(s, payload, $"alert.fired channel={alert.ChannelName}", cancellationToken);
    }

    public Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.WebhookUrl)) return Task.CompletedTask;

        var payload = new
        {
            @event = "alert.recovered",
            channelName = recovery.ChannelName,
            currentFailRate = recovery.CurrentFailRate,
            priorFailRate = recovery.PriorFailRate,
            threshold = recovery.Threshold,
            alertingDurationMinutes = recovery.AlertingDuration.TotalMinutes,
            firstFiredUtc = recovery.FirstFiredUtc.ToString("O"),
            lastFiredUtc = recovery.LastFiredUtc.ToString("O"),
            totalCalls = recovery.TotalCalls,
            successCount = recovery.SuccessCount,
            failureCount = recovery.FailureCount,
            recoveredAtUtc = DateTime.UtcNow.ToString("O"),
        };

        return PostJsonAsync(s, payload, $"alert.recovered channel={recovery.ChannelName}", cancellationToken);
    }

    private async Task PostJsonAsync<T>(
        WebhookTelemetryAlertSettings s,
        T payload,
        string contextLog,
        CancellationToken cancellationToken)
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, s.WebhookUrl)
        {
            Content = content,
        };

        if (!string.IsNullOrWhiteSpace(s.WebhookBearerToken))
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.WebhookBearerToken);
        }

        var signed = WebhookSigner.ComputeSignedHeaders(s.WebhookHmacSecret, bodyBytes);
        if (signed is { } sig)
        {
            requestMessage.Headers.TryAddWithoutValidation(WebhookSigner.TimestampHeaderName, sig.Timestamp);
            requestMessage.Headers.TryAddWithoutValidation(WebhookSigner.SignatureHeaderName, sig.Signature);
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Alert webhook returned non-success: status={Status} url={Url} context={Context}",
                (int)response.StatusCode, s.WebhookUrl, contextLog);
        }
    }
}
