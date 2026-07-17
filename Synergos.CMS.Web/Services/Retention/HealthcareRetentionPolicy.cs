using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retención de los registros clínicos PHI <c>App_Data/syn-healthcare/**/*.phi</c>
/// (ADR 0098): purga archivos cuya última modificación supera
/// <see cref="HealthcareSettings.RecordRetentionDays"/> (default 2190 = 6 años).
/// La auditoría de acceso PHI (<c>syn-audit</c>) NO se toca acá — retención
/// indefinida por obligación legal (la gestiona otra policy).
/// </summary>
public sealed class HealthcareRetentionPolicy : IRetentionPolicy
{
    private const string RootDir = "syn-healthcare";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<HealthcareSettings> _settings;
    private readonly ILogger<HealthcareRetentionPolicy> _logger;

    public HealthcareRetentionPolicy(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<HealthcareSettings> settings,
        ILogger<HealthcareRetentionPolicy> logger)
    {
        _hostEnvironment = hostEnvironment;
        _settings = settings;
        _logger = logger;
    }

    public string Name => "healthcare";

    public Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _settings.CurrentValue.RecordRetentionDays;
        if (retentionDays <= 0)
        {
            return Task.FromResult(0);
        }

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", RootDir);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult(0);
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var purged = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.phi", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    purged++;
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "HealthcareRetentionPolicy: no se pudo borrar {File}", file);
            }
        }
        return Task.FromResult(purged);
    }
}
