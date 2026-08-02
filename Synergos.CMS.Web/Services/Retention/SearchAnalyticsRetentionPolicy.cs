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
/// <b>Esta política es la que sostiene una promesa, no una limpieza cómoda.</b> Lo que hay en
/// ese directorio son <b>consultas escritas por visitantes</b> —pueden traer un nombre, una
/// dirección, un número de caso— y este barrido es lo único que hace que caduquen.
///
/// <para>Su productor es <c>FileSystemSearchAnalyticsStore</c>, que escribe una línea por
/// búsqueda en el archivo del día. <b>Los dos tienen que apuntar al mismo directorio</b>: si se
/// separan, se escribe en un sitio y se purga en otro, y nadie se entera hasta que el disco se
/// llena.</para>
///
/// <para>Antes de que ese almacén existiera, esta clase barría un directorio que nadie escribía
/// y parecía código muerto. No lo era, y borrarla entonces habría dejado la persistencia sin
/// red — la misma forma del defecto que ya apareció con la auditoría PHI.</para>
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
