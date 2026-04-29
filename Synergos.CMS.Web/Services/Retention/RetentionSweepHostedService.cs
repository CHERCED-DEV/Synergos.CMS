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
/// </remarks>
public sealed class RetentionSweepHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private readonly IEnumerable<IRetentionPolicy> _policies;
    private readonly ILogger<RetentionSweepHostedService> _logger;

    public RetentionSweepHostedService(
        IEnumerable<IRetentionPolicy> policies,
        ILogger<RetentionSweepHostedService> logger)
    {
        _policies = policies;
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
                try
                {
                    var purged = await policy.SweepAsync(stoppingToken);
                    if (purged > 0)
                    {
                        _logger.LogInformation(
                            "Retention sweep purged {Count} items from policy {Policy}",
                            purged, policy.Name);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Retention sweep failed for policy {Policy}", policy.Name);
                }
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
