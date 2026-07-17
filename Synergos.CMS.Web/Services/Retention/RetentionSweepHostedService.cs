using System.Diagnostics;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Background service que itera todas las <see cref="IRetentionPolicy"/>
/// registradas y dispara <c>SweepAsync</c> cada 24h. Reemplaza al
/// antiguo <c>AuditRetentionHostedService</c> generalizando el
/// pattern para N stores. Cap-270 Batch B (Ola 273).
/// </summary>
/// <remarks>
/// Try-catch per policy: una policy rota no afecta a las demás.
/// Initial delay de 60s para dejar boot terminar antes del primer sweep.
///
/// Cap-290 Batch B (Ola 294) — emite eventos <c>retention.swept</c>
/// (success) o <c>retention.failed</c> via <see cref="IAnalyticsTracker"/>
/// con metadata estructurada (policy, purged, durationMs, exception).
/// El operador ve visibilidad de liveness aún cuando purged=0 (confirma
/// que el sweep está corriendo). Hooked al sink default LoggerAnalyticsTracker
/// que emite log estructurado consumible por Serilog/AI/Elastic.
/// </remarks>
public sealed class RetentionSweepHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private readonly IEnumerable<IRetentionPolicy> _policies;
    private readonly IAnalyticsTracker _analytics;
    private readonly ILogger<RetentionSweepHostedService> _logger;

    public RetentionSweepHostedService(
        IEnumerable<IRetentionPolicy> policies,
        IAnalyticsTracker analytics,
        ILogger<RetentionSweepHostedService> logger)
    {
        _policies = policies;
        _analytics = analytics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var policy in _policies)
            {
                if (stoppingToken.IsCancellationRequested) return;
                await RunSinglePolicyAsync(policy, stoppingToken).ConfigureAwait(false);
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Ejecuta un policy individual con instrumentación completa.
    /// Internal para tests (no se podía testear el ExecuteAsync infinite
    /// loop directamente).
    /// </summary>
    internal async Task RunSinglePolicyAsync(
        IRetentionPolicy policy,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var purged = await policy.SweepAsync(ct);
            sw.Stop();
            if (purged > 0)
            {
                _logger.LogInformation(
                    "Retention sweep purged {Count} items from policy {Policy}",
                    purged, policy.Name);
            }
            _analytics.Track("retention.swept", new Dictionary<string, object?>
            {
                ["policy"] = policy.Name,
                ["purged"] = purged,
                ["durationMs"] = sw.Elapsed.TotalMilliseconds,
                ["success"] = true,
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Retention sweep failed for policy {Policy}", policy.Name);
            _analytics.Track("retention.failed", new Dictionary<string, object?>
            {
                ["policy"] = policy.Name,
                ["exception"] = ex.GetType().Name,
                ["message"] = ex.Message,
                ["durationMs"] = sw.Elapsed.TotalMilliseconds,
                ["success"] = false,
            });
        }
    }
}
