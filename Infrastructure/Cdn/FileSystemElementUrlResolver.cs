using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Configuration;

namespace Synergos.CMS.Infrastructure.Cdn;

/// <summary>
/// Filesystem-backed <see cref="IElementUrlResolver"/> implementation.
///
/// Resolution priority:
///   1. Dynamic overrides from <c>__dev-servers.json</c> (hot-reloaded via FileSystemWatcher)
///   2. Static overrides from <c>ElementServers:Overrides</c> in appsettings
///   3. Canonical CDN URL via <see cref="StaticUrlBuilder.ElementBundle"/>
///
/// The <c>__dev-servers.json</c> file lives alongside <c>registry.json</c> in the
/// CDN local path. UI dev tools write it on startup and clean it on exit, enabling
/// 1–N elements to be served from dev-servers simultaneously without CMS restarts.
///
/// Hot-reload follows the same proven pattern as <see cref="FileSystemArtifactResolver"/>:
/// FileSystemWatcher + 500 ms debounce + volatile dictionary swap.
///
/// Registered as Singleton (same lifetime as <see cref="StaticUrlBuilder"/>).
/// </summary>
public sealed class FileSystemElementUrlResolver : IElementUrlResolver, IDisposable
{
    private const string DevServersFileName = "__dev-servers.json";

    // Debounce window for FileSystemWatcher — coalesces multiple write events
    // that fire for a single logical save (e.g., editors that truncate+rewrite).
    private const int DevServersReloadDebounceMs = 500;

    private readonly StaticUrlBuilder _staticUrl;
    private readonly ElementServersSettings _staticOverrides;
    private readonly ILogger<FileSystemElementUrlResolver> _logger;
    private readonly string _devServersPath;
    private readonly FileSystemWatcher? _watcher;

    private volatile IReadOnlyDictionary<string, string> _devOverrides =
        new Dictionary<string, string>();

    public FileSystemElementUrlResolver(
        StaticUrlBuilder                             staticUrl,
        IOptions<ElementServersSettings>             servers,
        IOptions<StaticAssetsSettings>               assets,
        ILogger<FileSystemElementUrlResolver>        logger)
    {
        _staticUrl = staticUrl;
        _staticOverrides = servers.Value;
        _logger = logger;

        _devServersPath = Path.Combine(
            assets.Value.RegistryLocalPath,
            "synergos",
            DevServersFileName);

        LoadDevServers();
        _watcher = StartWatcher();
    }

    /// <summary>
    /// Resolves the bundle URL for a given element.
    ///   1. Dynamic override (__dev-servers.json) → {url}/main.js
    ///   2. Static override (appsettings) → {url}/main.js
    ///   3. CDN → StaticUrlBuilder.ElementBundle()
    /// </summary>
    public string ResolveBundle(string elementName)
    {
        if (_devOverrides.TryGetValue(elementName, out var dynamicUrl))
            return dynamicUrl.TrimEnd('/') + "/main.js";

        if (_staticOverrides.Overrides.TryGetValue(elementName, out var staticUrl))
            return staticUrl.TrimEnd('/') + "/main.js";

        return _staticUrl.ElementBundle(elementName);
    }

    // ── Dev-servers file loading ──────────────────────────────────────────────

    private void LoadDevServers()
    {
        if (!File.Exists(_devServersPath))
        {
            if (_devOverrides.Count > 0)
            {
                _devOverrides = new Dictionary<string, string>();
                _logger.LogInformation("Dev-servers file removed — all dynamic overrides cleared.");
            }
            return;
        }

        try
        {
            var json = File.ReadAllText(_devServersPath);
            var doc = JsonSerializer.Deserialize<DevServersFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (doc?.Servers is null or { Count: 0 })
            {
                _devOverrides = new Dictionary<string, string>();
                _logger.LogInformation("Dev-servers file is empty — dynamic overrides cleared.");
                return;
            }

            var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, info) in doc.Servers)
            {
                if (!string.IsNullOrEmpty(info.Url))
                    next[name] = info.Url;
            }

            // Atomic swap — readers see old or new, never partial state.
            _devOverrides = next;

            var summary = string.Join(", ", next.Select(kv => $"{kv.Key}→{kv.Value}"));
            _logger.LogInformation("Dev servers loaded: {Count} override(s) — {Summary}", next.Count, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load {Path}. Keeping previous overrides.", _devServersPath);
        }
    }

    // ── Hot-reload ────────────────────────────────────────────────────────────

    private FileSystemWatcher? StartWatcher()
    {
        var dir = Path.GetDirectoryName(_devServersPath);

        if (dir is null || !Directory.Exists(dir))
        {
            _logger.LogDebug("Directory {Dir} does not exist — dev-servers watcher skipped.", dir);
            return null;
        }

        var watcher = new FileSystemWatcher(dir, DevServersFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        // Debounce: FileSystemWatcher may fire multiple events for a single write.
        var debounceTimer = new System.Threading.Timer(_ =>
        {
            _logger.LogDebug("Dev-servers file changed — reloading.");
            LoadDevServers();
        }, null, Timeout.Infinite, Timeout.Infinite);

        watcher.Changed += (_, _) => debounceTimer.Change(DevServersReloadDebounceMs, Timeout.Infinite);
        watcher.Created += (_, _) => debounceTimer.Change(DevServersReloadDebounceMs, Timeout.Infinite);
        watcher.Deleted += (_, _) => debounceTimer.Change(DevServersReloadDebounceMs, Timeout.Infinite);

        _logger.LogDebug("FileSystemWatcher active on {Path}.", _devServersPath);
        return watcher;
    }

    public void Dispose() => _watcher?.Dispose();
}

// ── JSON deserialization models ─────────────────────────────────────────────

internal sealed class DevServersFile
{
    public Dictionary<string, DevServerEntry>? Servers { get; set; }
}

internal sealed class DevServerEntry
{
    public string Url { get; set; } = string.Empty;
    public string? Framework { get; set; }
    public int? Pid { get; set; }
    public string? StartedAt { get; set; }
}
