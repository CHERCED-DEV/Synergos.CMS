using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Slack-shaped para <see cref="IAlertNotifierChannel"/>. Block
/// Kit con colores diferenciados (rojo alert, verde recovery). Cap-260
/// Batch B (Ola 255).
/// </summary>
public sealed class SlackAlertNotifier : IAlertNotifierChannel
{
    private const string HttpClientName = "alert-slack";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<SlackAlertNotifier> _logger;

    public SlackAlertNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<SlackAlertNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.SlackWebhookUrl)) return;

        var payload = new
        {
            text = $"🚨 Webhook channel '{alert.ChannelName}' fail rate breach",
            blocks = new object[]
            {
                new { type = "header", text = new { type = "plain_text", text = $"🚨 Channel breach · {alert.ChannelName}" } },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Fail rate:*\n{alert.FailRate:P1} (threshold {alert.Threshold:P1})" },
                        new { type = "mrkdwn", text = $"*Total calls:*\n{alert.TotalCalls}" },
                        new { type = "mrkdwn", text = $"*Failures:*\n{alert.FailureCount}" },
                        new { type = "mrkdwn", text = $"*P95 latency:*\n{alert.P95LatencyMs:F0} ms" },
                    },
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new { type = "mrkdwn", text = $"_Cooldown {alert.CooldownMinutes} min — recovery notification on auto-resolve._" },
                    },
                },
            },
        };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        await SlackWebhookSender.SendAsync(httpClient, s.SlackWebhookUrl, payload,
            _logger, $"alert.fired channel={alert.ChannelName}", cancellationToken);
    }

    public async Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.SlackWebhookUrl)) return;

        var payload = new
        {
            text = $"✅ Webhook channel '{recovery.ChannelName}' recovered",
            blocks = new object[]
            {
                new { type = "header", text = new { type = "plain_text", text = $"✅ Channel recovered · {recovery.ChannelName}" } },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Fail rate ahora:*\n{recovery.CurrentFailRate:P1} (threshold {recovery.Threshold:P1})" },
                        new { type = "mrkdwn", text = $"*Fail rate al disparar:*\n{recovery.PriorFailRate:P1}" },
                        new { type = "mrkdwn", text = $"*Duración alerting:*\n{recovery.AlertingDuration.TotalMinutes:F0} min" },
                        new { type = "mrkdwn", text = $"*Total calls:*\n{recovery.TotalCalls} ({recovery.SuccessCount} OK / {recovery.FailureCount} fail)" },
                    },
                },
            },
        };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        await SlackWebhookSender.SendAsync(httpClient, s.SlackWebhookUrl, payload,
            _logger, $"alert.recovered channel={recovery.ChannelName}", cancellationToken);
    }
}
