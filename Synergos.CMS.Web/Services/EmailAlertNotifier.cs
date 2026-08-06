using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal email para <see cref="IAlertNotifierChannel"/>. Envía el
/// alert/recovery a <see cref="WebhookTelemetryAlertSettings.AlertEmail"/>
/// con HTML body inline (no Razor template — el bridge no tiene
/// HttpContext). Cap-260 Batch B (Ola 255).
/// </summary>
public sealed class EmailAlertNotifier : IAlertNotifierChannel
{
    private readonly IEmailService _emailService;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<EmailAlertNotifier> _logger;

    public EmailAlertNotifier(
        IEmailService emailService,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<EmailAlertNotifier> logger)
    {
        _emailService = emailService;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.AlertEmail))
        {
            return;
        }

        var subject = $"[Synergos] Webhook channel '{alert.ChannelName}' fail rate breach";
        var body = $@"
<h2>Webhook telemetry alert</h2>
<p>Channel <code>{alert.ChannelName}</code> ha cruzado el threshold de fail rate.</p>
<table border=""1"" cellpadding=""6"" cellspacing=""0"">
<tr><th>Metric</th><th>Value</th></tr>
<tr><td>Fail rate</td><td>{alert.FailRate:P1} (threshold {alert.Threshold:P1})</td></tr>
<tr><td>Total calls (last 1000)</td><td>{alert.TotalCalls}</td></tr>
<tr><td>Success</td><td>{alert.SuccessCount}</td></tr>
<tr><td>Failure</td><td>{alert.FailureCount}</td></tr>
<tr><td>P50 latency</td><td>{alert.P50LatencyMs:F0} ms</td></tr>
<tr><td>P95 latency</td><td>{alert.P95LatencyMs:F0} ms</td></tr>
<tr><td>P99 latency</td><td>{alert.P99LatencyMs:F0} ms</td></tr>
<tr><td>Last call (UTC)</td><td>{alert.LastObservedUtc:O}</td></tr>
</table>
<p>Acciones sugeridas:</p>
<ul>
<li>Revisar logs del receiver del canal.</li>
<li>Considerar tuning <code>WebhookResilience.PerChannel.{alert.ChannelName}</code> en appsettings.</li>
<li>Verificar /admin/health para correlation con otras métricas.</li>
</ul>
<p><small>Cooldown {alert.CooldownMinutes} min — no se re-alertará hasta que pase ese window. Recibirás un &quot;recovery&quot; cuando el canal vuelva bajo threshold.</small></p>
";
        try
        {
            await _emailService.SendAsync(new EmailMessage(
                To: s.AlertEmail,
                Subject: subject,
                BodyHtml: body),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Email alert notification failed: channel={Channel} to={To}",
                alert.ChannelName, s.AlertEmail);
        }
    }

    public async Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
    {
        var s = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(s.AlertEmail))
        {
            return;
        }

        var subject = $"[Synergos] Webhook channel '{recovery.ChannelName}' recovered";
        var body = $@"
<h2>Webhook telemetry recovery</h2>
<p>Channel <code>{recovery.ChannelName}</code> volvió bajo threshold y está
de nuevo healthy. Cierra el ciclo del alert previo — sin acción
requerida más allá de revisar la causa root si fue una incidencia
significativa.</p>
<table border=""1"" cellpadding=""6"" cellspacing=""0"">
<tr><th>Metric</th><th>Value</th></tr>
<tr><td>Fail rate actual</td><td>{recovery.CurrentFailRate:P1} (threshold {recovery.Threshold:P1})</td></tr>
<tr><td>Fail rate al disparar</td><td>{recovery.PriorFailRate:P1}</td></tr>
<tr><td>Duración alerting</td><td>{recovery.AlertingDuration.TotalMinutes:F0} min</td></tr>
<tr><td>First fired (UTC)</td><td>{recovery.FirstFiredUtc:O}</td></tr>
<tr><td>Last fired (UTC)</td><td>{recovery.LastFiredUtc:O}</td></tr>
<tr><td>Total calls (last 1000)</td><td>{recovery.TotalCalls}</td></tr>
<tr><td>Success</td><td>{recovery.SuccessCount}</td></tr>
<tr><td>Failure</td><td>{recovery.FailureCount}</td></tr>
</table>
<p><small>Recibido porque <code>WebhookTelemetryAlerts.RecoveryEmailEnabled</code> es true. Set a false en appsettings para silenciar este tipo de email.</small></p>
";
        try
        {
            await _emailService.SendAsync(new EmailMessage(
                To: s.AlertEmail,
                Subject: subject,
                BodyHtml: body),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Email recovery notification failed: channel={Channel} to={To}",
                recovery.ChannelName, s.AlertEmail);
        }
    }
}
