using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background service que purga archivos del audit trail más viejos
/// que <see cref="AdminSettings.AuditRetentionDays"/>. Corre al boot
/// + cada 24 horas. Ola 162.
/// </summary>
/// <remarks>
/// Si <c>AuditRetentionDays</c> es 0, el service no borra nada (el
/// operador gestiona retention manualmente).
///
/// Idempotent — solo borra archivos cuyo nombre matchea
/// <c>{yyyy-MM-dd}.jsonl</c> y cuya fecha parsed es más vieja que el
/// cutoff. Si el archivo no parsea (e.g., editado a mano), se ignora.
/// </remarks>
public sealed class AuditRetentionHostedService : BackgroundService
{
    private const string DirectoryName = "syn-audit";
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<AdminSettings> _adminSettings;
    private readonly ILogger<AuditRetentionHostedService> _logger;

    public AuditRetentionHostedService(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<AdminSettings> adminSettings,
        ILogger<AuditRetentionHostedService> logger)
    {
        _hostEnvironment = hostEnvironment;
        _adminSettings = adminSettings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit retention sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Sweep()
    {
        var retentionDays = _adminSettings.CurrentValue.AuditRetentionDays;
        if (retentionDays <= 0)
        {
            return;
        }

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", DirectoryName);
        if (!Directory.Exists(dir))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
        var purged = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
        {
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
                    _logger.LogWarning(ex,
                        "Failed to delete audit file {File}", file);
                }
            }
        }

        if (purged > 0)
        {
            _logger.LogInformation(
                "Audit retention sweep purged {Count} files older than {Days} days",
                purged, retentionDays);
        }
    }
}
