using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
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
            var path = ResolvePath(evt.OccurredAtUtc);
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

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", DirectoryName);
        if (!Directory.Exists(dir)) return Array.Empty<AuditEvent>();

        var results = new List<AuditEvent>();
        var files = Directory.EnumerateFiles(dir, "*.jsonl")
            .OrderByDescending(f => f)
            .Take(7)
            .ToList();

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

    private string ResolvePath(DateTime occurredAtUtc)
    {
        var fileName = occurredAtUtc.ToString("yyyy-MM-dd") + ".jsonl";
        return Path.Combine(
            _hostEnvironment.ContentRootPath,
            "App_Data",
            DirectoryName,
            fileName);
    }
}
