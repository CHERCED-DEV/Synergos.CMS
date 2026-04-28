using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lógica pura del telemetry alert scan — extraído del
/// <see cref="WebhookTelemetryAlertHostedService"/> para hacerse
/// testable sin BackgroundService timing. Olas 236-237 (Cap-240
/// Batch C) + refactor Cap-260 Batch B (Olas 254-256) para
/// dispatchear via <see cref="IAlertNotifier"/> composite en lugar
/// de <see cref="IEmailService"/> directo.
/// </summary>
/// <remarks>
/// Mantiene state in-memory de qué canales están actualmente en
/// "alerting state" (firing) y emite:
///
/// 1. <b>Alert</b> — cuando un canal cruza
///    <see cref="WebhookTelemetryAlertSettings.FailRateThreshold"/>
///    por primera vez o cuando re-alerta tras pasar el cooldown.
/// 2. <b>Recovery</b> — cuando un canal previamente alerting vuelve
///    a healthy (failRate &lt; threshold con sample suficiente).
///
/// State interno se limpia con el recovery — el ciclo siguiente
/// empieza fresh. La distribución a canales (email/slack/discord/teams/
/// webhook) la maneja el <see cref="IAlertNotifier"/> composite.
/// </remarks>
public sealed class WebhookTelemetryAlertScanner
{
    private readonly IWebhookTelemetryStore _telemetry;
    private readonly IAlertNotifier _notifier;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<WebhookTelemetryAlertScanner> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<string, ChannelAlertState> _alertState = new();

    public WebhookTelemetryAlertScanner(
        IWebhookTelemetryStore telemetry,
        IAlertNotifier notifier,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<WebhookTelemetryAlertScanner> logger,
        TimeProvider? timeProvider = null)
    {
        _telemetry = telemetry;
        _notifier = notifier;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ScanAndDispatchAsync(CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (!s.Enabled)
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
                        var recovery = new WebhookRecoveryEvent(
                            ChannelName: stat.ChannelName,
                            CurrentFailRate: failRate,
                            PriorFailRate: prior.LastFailRate,
                            Threshold: s.FailRateThreshold,
                            AlertingDuration: now - prior.FirstFiredUtc,
                            FirstFiredUtc: prior.FirstFiredUtc,
                            LastFiredUtc: prior.LastFiredUtc,
                            TotalCalls: stat.TotalCalls,
                            SuccessCount: stat.SuccessCount,
                            FailureCount: stat.FailureCount);
                        await _notifier.NotifyRecoveryAsync(recovery, cancellationToken);
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

            var alert = new WebhookAlertEvent(
                ChannelName: stat.ChannelName,
                FailRate: failRate,
                Threshold: s.FailRateThreshold,
                TotalCalls: stat.TotalCalls,
                SuccessCount: stat.SuccessCount,
                FailureCount: stat.FailureCount,
                P50LatencyMs: stat.P50LatencyMs,
                P95LatencyMs: stat.P95LatencyMs,
                P99LatencyMs: stat.P99LatencyMs,
                LastObservedUtc: stat.LastObservedUtc,
                CooldownMinutes: s.CooldownMinutes);
            await _notifier.NotifyAlertAsync(alert, cancellationToken);

            _logger.LogWarning(
                "Webhook telemetry alert sent for channel {Channel} failRate={FailRate:P1} totalCalls={Total}",
                stat.ChannelName, failRate, stat.TotalCalls);
        }
    }

    private sealed class ChannelAlertState
    {
        public DateTime FirstFiredUtc { get; init; }
        public DateTime LastFiredUtc { get; set; }
        public double LastFailRate { get; set; }
    }
}
