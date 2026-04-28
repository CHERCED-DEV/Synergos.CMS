using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lógica pura del telemetry alert scan — extraído del
/// <see cref="WebhookTelemetryAlertHostedService"/> para hacerse
/// testable sin BackgroundService timing. Olas 236-237 (Cap-240
/// Batch C). El hosted service queda como thin loop que invoca a
/// <see cref="ScanAndDispatchAsync"/> en cada tick.
/// </summary>
/// <remarks>
/// Mantiene state in-memory de qué canales están actualmente en
/// "alerting state" (firing) y emite:
///
/// 1. <b>Alert email</b> — cuando un canal cruza
///    <see cref="WebhookTelemetryAlertSettings.FailRateThreshold"/>
///    por primera vez o cuando re-alerta tras pasar el cooldown.
/// 2. <b>Recovery email</b> — cuando un canal previamente alerting
///    vuelve a healthy (failRate &lt; threshold con sample
///    suficiente). Cierra deferred §11.20 #10.
///
/// State interno se limpia con el recovery — el ciclo siguiente
/// empieza fresh.
/// </remarks>
public sealed class WebhookTelemetryAlertScanner
{
    private readonly IWebhookTelemetryStore _telemetry;
    private readonly IEmailService _emailService;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<WebhookTelemetryAlertScanner> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<string, ChannelAlertState> _alertState = new();

    public WebhookTelemetryAlertScanner(
        IWebhookTelemetryStore telemetry,
        IEmailService emailService,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<WebhookTelemetryAlertScanner> logger,
        TimeProvider? timeProvider = null)
    {
        _telemetry = telemetry;
        _emailService = emailService;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ScanAndDispatchAsync(CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (!s.Enabled || string.IsNullOrWhiteSpace(s.AlertEmail))
        {
            return;
        }

        var stats = _telemetry.GetChannelStats();
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, s.CooldownMinutes));
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var stat in stats)
        {
            var hasSample = stat.TotalCalls >= s.MinimumSampleSize;
            var failRate = stat.TotalCalls > 0
                ? (double)stat.FailureCount / stat.TotalCalls
                : 0;

            // Recovery first: si está en alerting state y volvió a
            // healthy con sample suficiente, dispara recovery + clear.
            if (_alertState.TryGetValue(stat.ChannelName, out var prior))
            {
                if (hasSample && failRate < s.FailRateThreshold)
                {
                    if (s.RecoveryEmailEnabled)
                    {
                        await SendRecoveryAsync(s, stat, prior, failRate, cancellationToken);
                    }
                    _alertState.TryRemove(stat.ChannelName, out _);
                    _logger.LogInformation(
                        "Webhook telemetry channel {Channel} recovered: failRate={FailRate:P1} (threshold {Threshold:P1})",
                        stat.ChannelName, failRate, s.FailRateThreshold);
                    continue;
                }
            }

            if (!hasSample) continue;
            if (failRate < s.FailRateThreshold) continue;

            // Threshold breached — fire alert respecting cooldown.
            if (_alertState.TryGetValue(stat.ChannelName, out var existing))
            {
                if (now - existing.LastFiredUtc < cooldown) continue;
                existing.LastFiredUtc = now;
                existing.LastFailRate = failRate;
            }
            else
            {
                _alertState[stat.ChannelName] = new ChannelAlertState
                {
                    FirstFiredUtc = now,
                    LastFiredUtc = now,
                    LastFailRate = failRate,
                };
            }

            await SendAlertAsync(s, stat, failRate, cancellationToken);
            _logger.LogWarning(
                "Webhook telemetry alert sent for channel {Channel} failRate={FailRate:P1} totalCalls={Total}",
                stat.ChannelName, failRate, stat.TotalCalls);
        }
    }

    private Task SendAlertAsync(
        WebhookTelemetryAlertSettings s,
        ChannelTelemetrySnapshot stat,
        double failRate,
        CancellationToken cancellationToken)
    {
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
<p><small>Cooldown {s.CooldownMinutes} min — no se re-alertará hasta que pase ese window. Recibirás un &quot;recovery&quot; cuando el canal vuelva bajo threshold.</small></p>
";
        return _emailService.SendAsync(new EmailMessage(
            To: s.AlertEmail,
            Subject: subject,
            BodyHtml: body),
            cancellationToken);
    }

    private Task SendRecoveryAsync(
        WebhookTelemetryAlertSettings s,
        ChannelTelemetrySnapshot stat,
        ChannelAlertState prior,
        double currentFailRate,
        CancellationToken cancellationToken)
    {
        var alertingDuration = _timeProvider.GetUtcNow().UtcDateTime - prior.FirstFiredUtc;
        var subject = $"[Synergos] Webhook channel '{stat.ChannelName}' recovered";
        var body = $@"
<h2>Webhook telemetry recovery</h2>
<p>Channel <code>{stat.ChannelName}</code> volvió bajo threshold y está
de nuevo healthy. Cierra el ciclo del alert previo — sin acción
requerida más allá de revisar la causa root si fue una incidencia
significativa.</p>
<table border=""1"" cellpadding=""6"" cellspacing=""0"">
<tr><th>Metric</th><th>Value</th></tr>
<tr><td>Fail rate actual</td><td>{currentFailRate:P1} (threshold {s.FailRateThreshold:P1})</td></tr>
<tr><td>Fail rate al disparar</td><td>{prior.LastFailRate:P1}</td></tr>
<tr><td>Duración alerting</td><td>{alertingDuration.TotalMinutes:F0} min</td></tr>
<tr><td>First fired (UTC)</td><td>{prior.FirstFiredUtc:O}</td></tr>
<tr><td>Last fired (UTC)</td><td>{prior.LastFiredUtc:O}</td></tr>
<tr><td>Total calls (last 1000)</td><td>{stat.TotalCalls}</td></tr>
<tr><td>Success</td><td>{stat.SuccessCount}</td></tr>
<tr><td>Failure</td><td>{stat.FailureCount}</td></tr>
</table>
<p><small>Recibido porque <code>WebhookTelemetryAlerts.RecoveryEmailEnabled</code> es true. Set a false en appsettings para silenciar este tipo de email.</small></p>
";
        return _emailService.SendAsync(new EmailMessage(
            To: s.AlertEmail,
            Subject: subject,
            BodyHtml: body),
            cancellationToken);
    }

    private sealed class ChannelAlertState
    {
        public DateTime FirstFiredUtc { get; init; }
        public DateTime LastFiredUtc { get; set; }
        public double LastFailRate { get; set; }
    }
}
