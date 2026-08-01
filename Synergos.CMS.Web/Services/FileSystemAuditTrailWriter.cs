using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IAuditTrailWriter"/> que persiste JSONL append-only
/// en <c>App_Data/syn-audit/{yyyy-MM-dd}.jsonl</c> — un archivo por día
/// para limitar el size de cada file y simplificar retention.
/// </summary>
/// <remarks>
/// Concurrency: lock interno per-day-file. Append es atómico a nivel de
/// línea (1 JSON por write). Reads cargan los últimos N días de archivos
/// en memoria — para volumen muy alto, swap por DB.
/// </remarks>
public sealed class FileSystemAuditTrailWriter : IAuditTrailWriter
{
    private const string DirectoryName = "syn-audit";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<FileSystemAuditTrailWriter> _logger;
    private readonly object _writeLock = new();

    public FileSystemAuditTrailWriter(
        IHostEnvironment hostEnvironment,
        ILogger<FileSystemAuditTrailWriter> logger)
    {
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var path = ResolvePath(evt.OccurredAtUtc, evt.Action);
            var line = JsonSerializer.Serialize(evt) + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(line);

            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8);
                    if (existing.Contains($"\"Id\":\"{evt.Id}\"", StringComparison.Ordinal))
                    {
                        return Task.CompletedTask;
                    }
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist audit event id={Id} action={Action}",
                evt.Id, evt.Action);
            return Task.CompletedTask;
        }
    }

    public IReadOnlyList<AuditEvent> GetRecent(
        int maxItems,
        string? actorEmailFilter = null,
        string? actionFilter = null)
    {
        if (maxItems <= 0) return Array.Empty<AuditEvent>();
        var files = ListJsonlFiles().OrderByDescending(f => f).Take(7).ToList();
        return ReadFiltered(files, maxItems, actorEmailFilter, actionFilter);
    }

    public AuditEvent? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var match = $"\"Id\":\"{id}\"";
        var files = ListJsonlFiles().OrderByDescending(f => f).Take(30).ToList();
        foreach (var file in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file, Encoding.UTF8); }
            catch (IOException) { continue; }
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.Contains(match, StringComparison.Ordinal)) continue;
                try { return JsonSerializer.Deserialize<AuditEvent>(line); }
                catch (JsonException) { continue; }
            }
        }
        return null;
    }

    public IReadOnlyList<AuditEvent> GetByDateRange(
        DateTime fromUtc,
        DateTime toUtc,
        int maxItems,
        string? actorEmailFilter = null,
        string? actionFilter = null)
    {
        if (maxItems <= 0) return Array.Empty<AuditEvent>();
        var fromDate = fromUtc.Date;
        var toDate = toUtc.Date;

        var files = new List<string>();
        foreach (var file in ListJsonlFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var fileDate)
                && fileDate >= fromDate && fileDate <= toDate)
            {
                files.Add(file);
            }
        }
        files.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal));

        var raw = ReadFiltered(files, maxItems * 2, actorEmailFilter, actionFilter);
        // Refinar al filtro fino dentro del día: only events with
        // OccurredAtUtc dentro del rango.
        return raw.Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= toUtc)
            .Take(maxItems)
            .ToList();
    }

    private IEnumerable<string> ListJsonlFiles()
    {
        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", DirectoryName);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*.jsonl");
    }

    private static List<AuditEvent> ReadFiltered(
        List<string> files,
        int maxItems,
        string? actorEmailFilter,
        string? actionFilter)
    {
        var results = new List<AuditEvent>();
        foreach (var file in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file, Encoding.UTF8);
            }
            catch (IOException)
            {
                continue;
            }

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                AuditEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<AuditEvent>(line);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (evt is null) continue;

                if (!string.IsNullOrWhiteSpace(actorEmailFilter) &&
                    !string.Equals(evt.ActorEmail, actorEmailFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(actionFilter) &&
                    !evt.Action.Contains(actionFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                results.Add(evt);
                if (results.Count >= maxItems) return results;
            }
        }
        return results;
    }

    /// <summary>
    /// El archivo del día, separando la auditoría de acceso a PHI de la administrativa.
    /// </summary>
    /// <remarks>
    /// <b>Dos familias de archivo porque tienen dos retenciones distintas</b>, y la purga
    /// borra archivos enteros. Mientras todo caía en <c>{fecha}.jsonl</c>, la auditoría de
    /// quién miró una historia clínica se iba a los 90 días con la administrativa — aunque el
    /// XML-doc de <c>HealthcareRetentionPolicy</c> afirmara que era indefinida. Ver ADR 0121.
    ///
    /// <para>El discriminador es el prefijo <c>phi.</c> del <see cref="AuditEvent.Action"/>,
    /// que ya existía y lo escribe <c>DefaultPhiAccessGuard</c>. No hace falta un campo nuevo
    /// en el evento.</para>
    ///
    /// <para><b>Los lectores no se enteran:</b> el visor de administración lista
    /// <c>*.jsonl</c>, y <c>{fecha}.phi.jsonl</c> también casa con ese patrón. Separar el
    /// archivo cambia qué se PURGA, no qué se ve.</para>
    /// </remarks>
    internal static string ResolveFileName(DateTime occurredAtUtc, string? action)
    {
        var day = occurredAtUtc.ToString("yyyy-MM-dd");
        return IsPhiAudit(action) ? day + PhiSuffix : day + ".jsonl";
    }

    /// <summary>Sufijo de los archivos de auditoría PHI, con su propia retención.</summary>
    internal const string PhiSuffix = ".phi.jsonl";

    /// <summary>
    /// Si el evento es un acceso a PHI. El prefijo <c>phi.</c> lo pone el guard clínico.
    /// </summary>
    internal static bool IsPhiAudit(string? action)
        => action is not null && action.StartsWith("phi.", StringComparison.OrdinalIgnoreCase);

    private string ResolvePath(DateTime occurredAtUtc, string? action)
    {
        var fileName = ResolveFileName(occurredAtUtc, action);
        return Path.Combine(
            _hostEnvironment.ContentRootPath,
            "App_Data",
            DirectoryName,
            fileName);
    }
}
