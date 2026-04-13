using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UmbracoHosting = Umbraco.Cms.Core.Hosting.IHostingEnvironment;

namespace Synergos.CMS.Infrastructure.USync;

/// <summary>
/// Keeps the uSync/v9/Content and uSync/v9/Media folders free of duplicate config
/// files — i.e. multiple .config files that share the same root element Key (GUID).
///
/// Duplicates arise when:
///   • A content/media item is renamed in the backoffice: uSync exports a new file
///     with the new alias but leaves the old file in place.
///   • A media item is deleted and re-uploaded with the same name: the old config
///     file remains on disk next to the new one.
///
/// Strategy:
///   • DeduplicateAll()  — called at startup, before uSync ImportOnStartup runs.
///                         Groups all files by root GUID, keeps the newest, deletes
///                         the rest.  Idempotent and safe to run on every boot.
///   • DeleteOrphanedFile() — called when content/media is deleted from the
///                            backoffice.  Removes its config file immediately so
///                            a subsequent re-upload cannot produce a GUID clash.
/// </summary>
public sealed class USyncFileCleanerService
{
    private static readonly Regex RootKeyRegex =
        new(@"<(?:Content|Media)\s[^>]*Key=""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));

    private readonly UmbracoHosting _hosting;
    private readonly ILogger<USyncFileCleanerService> _logger;

    public USyncFileCleanerService(
        UmbracoHosting hosting,
        ILogger<USyncFileCleanerService> logger)
    {
        _hosting = hosting;
        _logger = logger;
    }

    private string USyncRoot => Path.Combine(_hosting.ApplicationPhysicalPath, "uSync", "v9");

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Scans Content/ and Media/ for files that share the same root GUID.
    /// Keeps the most-recently-modified file; deletes all others.
    /// Returns the number of files removed.
    /// </summary>
    public int DeduplicateAll()
    {
        var removed = 0;

        foreach (var folder in new[] { "Content", "Media" })
        {
            var dir = Path.Combine(USyncRoot, folder);
            if (!Directory.Exists(dir)) continue;

            var byKey = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.EnumerateFiles(dir, "*.config", SearchOption.AllDirectories))
            {
                var key = ReadRootKey(path);
                if (key is null || key == "00000000-0000-0000-0000-000000000000") continue;

                if (!byKey.TryGetValue(key, out var list))
                    byKey[key] = list = [];

                list.Add(new FileInfo(path));
            }

            foreach (var (key, files) in byKey)
            {
                if (files.Count <= 1) continue;

                var ordered = files.OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                var keep    = ordered[0];

                foreach (var stale in ordered.Skip(1))
                {
                    try
                    {
                        stale.Delete();
                        removed++;
                        _logger.LogWarning(
                            "[uSync cleaner] Removed stale duplicate '{Stale}' " +
                            "(superseded by '{Keep}', GUID {Key}).",
                            stale.Name, keep.Name, key);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[uSync cleaner] Could not delete stale file '{File}'.", stale.FullName);
                    }
                }
            }
        }

        if (removed > 0)
            _logger.LogWarning(
                "[uSync cleaner] Removed {Count} stale duplicate config file(s). " +
                "Run Export from the backoffice to regenerate clean files.", removed);
        else
            _logger.LogDebug("[uSync cleaner] No duplicate config files found.");

        return removed;
    }

    /// <summary>
    /// Removes the uSync config file(s) that belong to a deleted content or media
    /// item identified by <paramref name="key"/>.
    /// </summary>
    public void DeleteOrphanedFile(Guid key, bool isMedia)
    {
        var keyStr = key.ToString();
        var folder = isMedia ? "Media" : "Content";
        var dir    = Path.Combine(USyncRoot, folder);

        if (!Directory.Exists(dir)) return;

        foreach (var path in Directory.EnumerateFiles(dir, "*.config", SearchOption.AllDirectories))
        {
            if (!string.Equals(ReadRootKey(path), keyStr, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(path);
                _logger.LogInformation(
                    "[uSync cleaner] Deleted orphaned config '{File}' for deleted {Type} {Key}.",
                    Path.GetFileName(path), isMedia ? "media" : "content", keyStr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[uSync cleaner] Could not delete orphaned file '{File}'.", path);
            }
        }
    }

    // ── Internal helpers ────────────────────────────────────────────────────

    private string? ReadRootKey(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var linesRead = 0;
            string? line;

            while ((line = reader.ReadLine()) != null && linesRead < 10)
            {
                linesRead++;
                var match = RootKeyRegex.Match(line);
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[uSync cleaner] Could not read '{File}'.", filePath);
        }

        return null;
    }
}
