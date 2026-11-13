using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retención de lo clínico: los registros PHI <c>App_Data/syn-healthcare/**/*.phi</c> y la
/// AUDITORÍA de acceso a PHI <c>App_Data/syn-audit/{fecha}.phi.jsonl</c> (ADR 0098, ADR 0121).
/// </summary>
/// <remarks>
/// <para><b>Los registros clínicos no se purgan</b> (ADR 0142). Pasado el término legal
/// (<see cref="HealthcareSettings.RetencionEfectivaDias"/>, 15 años como mínimo), un documento es CANDIDATO a
/// eliminarse, y su eliminación sigue el trámite del art. 4 de la Resolución 839 de 2017, que no es automático: esta
/// policy los cuenta y lo avisa, y no borra ninguno. Antes borraba todo <c>.phi</c> sin escribir en 6 años (la cifra
/// de HIPAA), por debajo del mínimo colombiano. La auditoría se purga según
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
        AvisarLosCandidatos(settings.RetencionEfectivaDias, cancellationToken);
        var purged = PurgeAccessAudit(settings.AccessAuditRetentionDays, cancellationToken);
        return Task.FromResult(purged);
    }

    /// <summary>
    /// Cuántos documentos clínicos pasaron el término legal. NO los borra: su eliminación es el trámite del art. 4
    /// (ADR 0142). Devuelve la cuenta, y si hay alguno lo avisa en el log para quien lleva ese trámite.
    /// </summary>
    /// <remarks>
    /// La fecha de la última escritura es una cota inferior de la última atención: un documento sin escribir en el
    /// término es candidato, y el trámite confirma la fecha de la última atención antes de eliminar nada.
    /// </remarks>
    internal int AvisarLosCandidatos(int retentionDays, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", RootDir);
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(retentionDays, HealthcareSettings.RetencionMinimaDias));
        var candidatos = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.phi", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                candidatos++;
            }
        }

        if (candidatos > 0)
        {
            _logger.LogWarning(
                "HealthcareRetentionPolicy: {Candidatos} documentos clínicos pasaron el término legal de conservación. " +
                "No se borran: su eliminación sigue el trámite del art. 4 de la Resolución 839 de 2017 (ADR 0142).",
                candidatos);
        }
        return candidatos;
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
