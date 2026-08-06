using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retention para los archivos de checkouts del Dashboard
/// <c>App_Data/syn-dashboard/orders/{yyyy-MM-dd}.jsonl</c> (ADR 0097 D5).
/// Espeja <see cref="AuditRetentionPolicy"/>: lee
/// <see cref="DashboardSettings.ProjectionRetentionDays"/>, purga archivos
/// por día anteriores al cutoff. Idempotente; 0 días = nunca purgar.
/// </summary>
/// <remarks>
/// La proyección (<c>projections/projection.jsonl</c>) NO necesita policy:
/// es un único archivo que se reescribe en cada flush aplicando la retención
/// in-place. Solo los órdenes acumulan archivos por día.
/// </remarks>
public sealed class DashboardOrdersRetentionPolicy : IRetentionPolicy
{
    private static readonly string[] DirSegments = { "App_Data", "syn-dashboard", "orders" };

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<DashboardSettings> _settings;
    private readonly ILogger<DashboardOrdersRetentionPolicy> _logger;

    public DashboardOrdersRetentionPolicy(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<DashboardSettings> settings,
        ILogger<DashboardOrdersRetentionPolicy> logger)
    {
        _hostEnvironment = hostEnvironment;
        _settings = settings;
        _logger = logger;
    }

    public string Name => "dashboard-orders";

    public Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _settings.CurrentValue.ProjectionRetentionDays;
        if (retentionDays <= 0)
        {
            return Task.FromResult(0);
        }

        var dir = Path.Combine(new[] { _hostEnvironment.ContentRootPath }.Concat(DirSegments).ToArray());
        if (!Directory.Exists(dir))
        {
            return Task.FromResult(0);
        }

        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
        var purged = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                continue;
            }
            if (fileDate < cutoff)
            {
                try
                {
                    File.Delete(file);
                    purged++;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to delete dashboard order file {File}", file);
                }
            }
        }

        return Task.FromResult(purged);
    }
}
