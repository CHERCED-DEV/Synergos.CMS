using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retención de lo clínico: los registros PHI <c>App_Data/syn-healthcare/**/*.phi</c> y la
/// AUDITORÍA de acceso a PHI <c>App_Data/syn-audit/{fecha}.phi.jsonl</c> (ADR 0098, ADR 0121).
/// </summary>
/// <remarks>
/// <para>Los registros se purgan según <see cref="HealthcareSettings.RecordRetentionDays"/>
/// (default 2190 = 6 años); la auditoría, según
/// <see cref="HealthcareSettings.AccessAuditRetentionDays"/> (default 0 = nunca).</para>
///
/// <para><b>Esta clase decía que la auditoría "la gestiona otra policy" y era falso.</b> Los
/// eventos <c>phi.*</c> caían en el mismo <c>{fecha}.jsonl</c> que la auditoría
/// administrativa, así que <c>AuditRetentionPolicy</c> los borraba a los 90 días mientras el
/// comentario de arriba prometía retención indefinida por obligación legal. Ahora el escritor
/// los separa en su propia familia de archivos y esta policy es de verdad quien los gestiona.
/// Ver ADR 0121.</para>
/// </remarks>
public sealed class HealthcareRetentionPolicy : IRetentionPolicy
{
    private const string RootDir = "syn-healthcare";
    private const string AuditDir = "syn-audit";

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
        var settings = _settings.CurrentValue;
        var purged = PurgeRecords(settings.RecordRetentionDays, cancellationToken);
        purged += PurgeAccessAudit(settings.AccessAuditRetentionDays, cancellationToken);
        return Task.FromResult(purged);
    }

    private int PurgeRecords(int retentionDays, CancellationToken cancellationToken)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", RootDir);
        if (!Directory.Exists(dir))
        {
            return 0;
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

        return purged;
    }

    /// <summary>
    /// Purga la auditoría de acceso a PHI. Con 0 (el default) no borra nada.
    /// </summary>
    /// <remarks>
    /// La fecha se toma del NOMBRE del archivo y no de su última escritura, igual que hace
    /// <c>AuditRetentionPolicy</c>: un archivo del día se reescribe cada vez que alguien
    /// entra a un expediente, y con la marca del sistema de archivos la retención se
    /// reiniciaría en cada acceso — el registro más consultado sería el último en borrarse.
    /// </remarks>
    private int PurgeAccessAudit(int retentionDays, CancellationToken cancellationToken)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", AuditDir);
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
        var purged = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*" + FileSystemAuditTrailWriter.PhiSuffix))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var day = Path.GetFileName(file);
            day = day[..Math.Max(0, day.Length - FileSystemAuditTrailWriter.PhiSuffix.Length)];
            if (!DateTime.TryParseExact(
                    day, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate)
                || fileDate >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                purged++;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "HealthcareRetentionPolicy: no se pudo borrar {File}", file);
            }
        }

        return purged;
    }
}
