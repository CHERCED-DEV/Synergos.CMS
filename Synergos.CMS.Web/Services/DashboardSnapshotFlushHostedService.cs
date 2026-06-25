using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background service que flushea periódicamente la proyección de métricas
/// (<see cref="InMemoryMetricsProjectionStore"/>) a JSONL (ADR 0097). NO
/// bloquea requests — el hot-path solo toca memoria; el IO vive aquí.
/// </summary>
/// <remarks>
/// Intervalo configurable vía <c>Synergos:Dashboard:FlushIntervalSeconds</c>
/// (mínimo efectivo 30s). Hace un flush final al apagar para no perder los
/// buckets de la última ventana. Si <c>Enabled=false</c>, no corre (la
/// proyección queda solo en memoria).
/// </remarks>
public sealed class DashboardSnapshotFlushHostedService : BackgroundService
{
    private readonly InMemoryMetricsProjectionStore _store;
    private readonly IOptions<DashboardSettings> _settings;
    private readonly ILogger<DashboardSnapshotFlushHostedService> _logger;

    public DashboardSnapshotFlushHostedService(
        InMemoryMetricsProjectionStore store,
        IOptions<DashboardSettings> settings,
        ILogger<DashboardSnapshotFlushHostedService> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Value.Enabled)
        {
            _logger.LogInformation(
                "DashboardSnapshotFlushHostedService: deshabilitado (Synergos:Dashboard:Enabled=false)");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, _settings.Value.FlushIntervalSeconds));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                await _store.FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado normal.
        }
        finally
        {
            // Flush final — preserva la última ventana sin esperar al próximo tick.
            try { await _store.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DashboardSnapshotFlushHostedService: flush final falló");
            }
        }
    }
}
