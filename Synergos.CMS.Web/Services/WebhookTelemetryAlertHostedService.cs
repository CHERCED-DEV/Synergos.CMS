using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background service que polléa <see cref="IWebhookTelemetryStore"/>
/// y dispara email al operador cuando algún canal cruza
/// <see cref="WebhookTelemetryAlertSettings.FailRateThreshold"/>.
/// Cooldown per canal evita spam. Olas 195-196.
/// </summary>
/// <remarks>
/// Diseño simple — usa <see cref="IEmailService"/> directo en lugar
/// de un composite notifier pattern. Si llega Rule of Three (Slack +
/// Discord + Teams para alerts), refactor a composite notifier
/// paralelo a los 3 dominios existentes (Comments / Forms / Cart).
/// </remarks>
public sealed class WebhookTelemetryAlertHostedService : BackgroundService
{
    private readonly IWebhookTelemetryStore _telemetry;
    private readonly IEmailService _emailService;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<WebhookTelemetryAlertHostedService> _logger;

    private readonly ConcurrentDictionary<string, DateTime> _lastAlertUtc = new();

    public WebhookTelemetryAlertHostedService(
        IWebhookTelemetryStore telemetry,
        IEmailService emailService,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<WebhookTelemetryAlertHostedService> logger)
    {
        _telemetry = telemetry;
        _emailService = emailService;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay para dejar boot terminar.
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var s = _settings.CurrentValue;
            if (!s.Enabled || string.IsNullOrWhiteSpace(s.AlertEmail))
            {
                try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, s.CheckIntervalMinutes)), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await ScanAndAlertAsync(s, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebhookTelemetryAlert scan failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, s.CheckIntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ScanAndAlertAsync(WebhookTelemetryAlertSettings s, CancellationToken cancellationToken)
    {
        var stats = _telemetry.GetChannelStats();
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, s.CooldownMinutes));
        var now = DateTime.UtcNow;

        foreach (var stat in stats)
        {
            if (stat.TotalCalls < s.MinimumSampleSize) continue;

            var failRate = stat.TotalCalls > 0
                ? (double)stat.FailureCount / stat.TotalCalls
                : 0;
            if (failRate < s.FailRateThreshold) continue;

            if (_lastAlertUtc.TryGetValue(stat.ChannelName, out var last) &&
                now - last < cooldown)
            {
                continue;
            }
            _lastAlertUtc[stat.ChannelName] = now;

            var subject = $"[Synergos] Webhook channel '{stat.ChannelName}' fail rate breach";
            var body = $@"
<h2>Webhook telemetry alert</h2>
<p>Channel <code>{stat.ChannelName}</code> ha cruzado el threshold de fail rate.</p>
<table border=""1"" cellpadding=""6"" cellspacing=""0"">
<tr><th>Metric</th><th>Value</th></tr>
<tr><td>Fail rate</td><td>{failRate:P1} (threshold {s.FailRateThreshold:P1})</td></tr>
<tr><td>Total calls (last 1000)</td><td>{stat.TotalCalls}</td></tr>
<tr><td>Success</td><td>{stat.SuccessCount}</td></tr>
<tr><td>Failure</td><td>{stat.FailureCount}</td></tr>
<tr><td>P50 latency</td><td>{stat.P50LatencyMs:F0} ms</td></tr>
<tr><td>P95 latency</td><td>{stat.P95LatencyMs:F0} ms</td></tr>
<tr><td>P99 latency</td><td>{stat.P99LatencyMs:F0} ms</td></tr>
<tr><td>Last call (UTC)</td><td>{stat.LastObservedUtc:O}</td></tr>
</table>
<p>Acciones sugeridas:</p>
<ul>
<li>Revisar logs del receiver del canal.</li>
<li>Considerar tuning <code>WebhookResilience.PerChannel.{stat.ChannelName}</code> en appsettings.</li>
<li>Verificar /admin/health para correlation con otras métricas.</li>
</ul>
<p><small>Cooldown {s.CooldownMinutes} min — no se re-alertará hasta que pase ese window.</small></p>
";

            await _emailService.SendAsync(new EmailMessage(
                To: s.AlertEmail,
                Subject: subject,
                BodyHtml: body),
                cancellationToken);

            _logger.LogWarning(
                "Webhook telemetry alert sent for channel {Channel} failRate={FailRate:P1} totalCalls={Total}",
                stat.ChannelName, failRate, stat.TotalCalls);
        }
    }
}
