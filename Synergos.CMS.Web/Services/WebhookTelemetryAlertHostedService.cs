using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background service que polléa <see cref="WebhookTelemetryAlertScanner"/>
/// y dispara emails de alert/recovery según corresponda. La lógica
/// pura del scan vive en el scanner — este service es solo el loop
/// + initial delay + cooldown entre ticks. Olas 195-196 + 236-237.
/// </summary>
public sealed class WebhookTelemetryAlertHostedService : BackgroundService
{
    private readonly WebhookTelemetryAlertScanner _scanner;
    private readonly IOptionsMonitor<WebhookTelemetryAlertSettings> _settings;
    private readonly ILogger<WebhookTelemetryAlertHostedService> _logger;

    public WebhookTelemetryAlertHostedService(
        WebhookTelemetryAlertScanner scanner,
        IOptionsMonitor<WebhookTelemetryAlertSettings> settings,
        ILogger<WebhookTelemetryAlertHostedService> logger)
    {
        _scanner = scanner;
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
            try
            {
                await _scanner.ScanAndDispatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebhookTelemetryAlert scan failed");
            }

            var s = _settings.CurrentValue;
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, s.CheckIntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
