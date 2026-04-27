using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background <see cref="IHostedService"/> que cada
/// <see cref="CartAbandonmentSettings.ScanInterval"/> invoca
/// <see cref="ICartAbandonmentTracker.DetectAbandoned"/> y emite
/// <c>cart.abandoned</c> events vía <see cref="IAnalyticsTracker"/>
/// para cada cart detectado.
/// </summary>
/// <remarks>
/// El operador puede consumir los logs estructurados (eventos
/// <c>cart.abandoned</c>) en su sink (Serilog → Elastic, AppInsights,
/// etc.) para disparar workflows de marketing automation
/// (recovery emails, retargeting ads, etc.). El tracker NO envía
/// emails directamente — ese es responsabilidad de un consumer
/// downstream que se suscribe a los eventos.
/// </remarks>
public sealed class CartAbandonmentScannerHostedService : BackgroundService
{
    private readonly ICartAbandonmentTracker _tracker;
    private readonly IAnalyticsTracker _analytics;
    private readonly IOptionsMonitor<CartAbandonmentSettings> _settings;
    private readonly ILogger<CartAbandonmentScannerHostedService> _logger;

    public CartAbandonmentScannerHostedService(
        ICartAbandonmentTracker tracker,
        IAnalyticsTracker analytics,
        IOptionsMonitor<CartAbandonmentSettings> settings,
        ILogger<CartAbandonmentScannerHostedService> logger)
    {
        _tracker = tracker;
        _analytics = analytics;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeño delay inicial para dejar boot terminar antes de
        // empezar el scan (cero scans en los primeros 30s).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = _settings.CurrentValue;

            if (current.Enabled)
            {
                try
                {
                    var abandoned = _tracker.DetectAbandoned(current.AbandonmentThreshold);
                    foreach (var cart in abandoned)
                    {
                        if (cart.Subtotal < current.MinSubtotalToReport)
                        {
                            continue;
                        }

                        _analytics.Track("cart.abandoned", new Dictionary<string, object?>
                        {
                            ["cartId"] = cart.CartId,
                            ["itemCount"] = cart.ItemCount,
                            ["subtotal"] = cart.Subtotal,
                            ["currency"] = cart.Currency,
                            ["lastActivityUtc"] = cart.LastActivityUtc,
                            ["minutesSinceActivity"] = (int)(DateTime.UtcNow - cart.LastActivityUtc).TotalMinutes,
                        });
                    }

                    if (abandoned.Count > 0)
                    {
                        _logger.LogInformation(
                            "Cart abandonment scan: {Count} carts reported",
                            abandoned.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Cart abandonment scan failed — siguiente intento en {Interval}",
                        current.ScanInterval);
                }
            }

            try
            {
                await Task.Delay(current.ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
