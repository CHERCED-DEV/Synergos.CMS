using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retention para <c>App_Data/syn-search-analytics/{yyyy-MM-dd}.jsonl</c>.
/// Misma shape que audit (filename = fecha) — purge por filename
/// parsing. Cap-270 Batch B (Ola 274).
/// </summary>
/// <remarks>
/// <b>Hoy no barre nada, y eso NO la vuelve código muerto.</b> Nadie escribe ese directorio:
/// el único productor de analítica de búsqueda es <c>InMemorySearchAnalyticsStore</c>, que
/// guarda agregados en memoria y nunca toca disco. La política corre igual —el barrido la
/// invoca cada 24h y sale en 0 porque el directorio no existe— y está cubierta por
/// <c>RetentionPolicyTests</c>, que le fabrica el directorio.
///
/// <para>Se conserva a propósito. El día que la analítica se persista, lo que se acumula son
/// <b>consultas escritas por visitantes</b> —nombres, direcciones, números de caso—: datos que
/// no pueden quedarse indefinidamente. Borrar esta política dejaría ese caso sin red, y es
/// exactamente la forma del defecto que ya apareció una vez con la auditoría PHI.</para>
///
/// <para>Si algún día se decide que la analítica NO se persistirá nunca, entonces sí: esta
/// clase, <c>RetentionSettings.SearchAnalyticsRetentionDays</c> y su test se van juntos.</para>
/// </remarks>
public sealed class SearchAnalyticsRetentionPolicy : IRetentionPolicy
{
    private const string DirectoryName = "syn-search-analytics";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<RetentionSettings> _settings;
    private readonly ILogger<SearchAnalyticsRetentionPolicy> _logger;

    public SearchAnalyticsRetentionPolicy(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<RetentionSettings> settings,
        ILogger<SearchAnalyticsRetentionPolicy> logger)
    {
        _hostEnvironment = hostEnvironment;
        _settings = settings;
        _logger = logger;
    }

    public string Name => "search-analytics";

    public Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _settings.CurrentValue.SearchAnalyticsRetentionDays;
        if (retentionDays <= 0) return Task.FromResult(0);

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", DirectoryName);
        if (!Directory.Exists(dir)) return Task.FromResult(0);

        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
        var purged = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var fileDate)) continue;
            if (fileDate < cutoff)
            {
                try { File.Delete(file); purged++; }
                catch (IOException ex) { _logger.LogWarning(ex, "Failed to delete search-analytics file {File}", file); }
            }
        }
        return Task.FromResult(purged);
    }
}
