using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Configuration;

namespace Synergos.CMS.Infrastructure.Cdn;

/// <summary>
/// Filesystem-backed <see cref="IArtifactResolver"/> implementation.
///
/// Loads the CDN manifest (<c>registry.json</c>) from a local directory at startup
/// and hot-reloads via <see cref="FileSystemWatcher"/> with a debounce window.
/// Reads are lock-free — a <see cref="volatile"/> dictionary reference is swapped
/// atomically after each reload, so concurrent callers always see a consistent snapshot.
///
/// Fallback: if the registry is missing or corrupt, all elements render SSR — no
/// throw, no partial state.
/// </summary>
public sealed class FileSystemArtifactResolver : IArtifactResolver, IDisposable
{
    // Debounce window for FileSystemWatcher — coalesces multiple write events
    // that fire for a single logical save (e.g., editors that truncate+rewrite).
    private const int RegistryReloadDebounceMs = 500;

    private readonly StaticAssetsSettings _settings;
    private readonly StaticUrlBuilder     _urlBuilder;
    private readonly ILogger<FileSystemArtifactResolver> _logger;
    private readonly HashSet<string> _clientHosted;
    private readonly FileSystemWatcher? _watcher;
    private volatile IReadOnlyDictionary<string, ArtifactInfo> _artifacts =
        new Dictionary<string, ArtifactInfo>();

    public FileSystemArtifactResolver(
        IOptions<StaticAssetsSettings>         options,
        StaticUrlBuilder                       urlBuilder,
        ILogger<FileSystemArtifactResolver>    logger)
    {
        _settings   = options.Value;
        _urlBuilder = urlBuilder;
        _logger     = logger;
        _clientHosted = new HashSet<string>(_settings.ClientHosted, StringComparer.OrdinalIgnoreCase);

        LoadRegistry();
        _watcher = StartWatcher();
    }

    public bool IsClientHosted(string alias) => _clientHosted.Contains(alias);

    public ArtifactInfo? ResolveByAlias(string alias)
        => _artifacts.TryGetValue(alias, out var info) ? info : null;

    /// <summary>
    /// Returns the distinct set of CSS URLs required by the currently loaded artifacts
    /// whose aliases are configured for client-hosting.
    /// </summary>
    public IEnumerable<string> GetClientStyleUrls()
        => _artifacts
            .Where(kv => _clientHosted.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value.StyleUrl))
            .Select(kv => kv.Value.StyleUrl!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    // ── Registry loading ──────────────────────────────────────────────────────

    private string ResolveRegistryPath()
        => Path.Combine(
            _settings.RegistryLocalPath,
            _settings.RegistryManifest.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private void LoadRegistry()
    {
        var registryPath = ResolveRegistryPath();

        if (!File.Exists(registryPath))
        {
            _logger.LogWarning("CDN registry not found at {Path}. Client-hosted elements will fall back to SSR.", registryPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(registryPath);
            var registry = JsonSerializer.Deserialize<CdnRegistry>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (registry?.Elements is null)
            {
                _logger.LogWarning("CDN registry at {Path} is empty or malformed.", registryPath);
                return;
            }

            var next = new Dictionary<string, ArtifactInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in registry.Elements)
            {
                if (string.IsNullOrEmpty(element.Alias)) continue;

                // Resolve version from implementations[framework][slot]
                var version = string.Empty;
                string? framework = null;
                if (element.Implementations is not null
                    && element.Implementations.TryGetValue(_settings.UiFramework, out var slots))
                {
                    framework = _settings.UiFramework;
                    slots.TryGetValue(_settings.UiSlot, out version!);
                    version ??= string.Empty;
                }

                var scriptUrl = _urlBuilder.ElementBundle(element.Name);

                next[element.Alias] = new ArtifactInfo(
                    Name:      element.Name,
                    Tag:       element.Tag,
                    ScriptUrl: scriptUrl,
                    StyleUrl:  null,
                    Integrity: null,
                    Version:   version,
                    Framework: framework);
            }

            // Atomic swap — readers see old or new, never partial state.
            _artifacts = next;

            _logger.LogInformation("CDN registry loaded: {Count} artifacts from {Path}.", next.Count, registryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load CDN registry from {Path}. Client-hosted elements will fall back to SSR.", registryPath);
        }
    }

    // ── Hot-reload ────────────────────────────────────────────────────────────

    private FileSystemWatcher? StartWatcher()
    {
        var registryPath = ResolveRegistryPath();
        var dir = Path.GetDirectoryName(registryPath);
        var file = Path.GetFileName(registryPath);

        if (dir is null || !Directory.Exists(dir)) return null;

        var watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        // Debounce: FileSystemWatcher may fire multiple events for a single write.
        var debounceTimer = new System.Threading.Timer(_ =>
        {
            _logger.LogInformation("CDN registry changed — reloading.");
            LoadRegistry();
        }, null, Timeout.Infinite, Timeout.Infinite);

        watcher.Changed += (_, _) => debounceTimer.Change(RegistryReloadDebounceMs, Timeout.Infinite);
        watcher.Created += (_, _) => debounceTimer.Change(RegistryReloadDebounceMs, Timeout.Infinite);

        _logger.LogDebug("FileSystemWatcher active on {Path}.", registryPath);
        return watcher;
    }

    public void Dispose() => _watcher?.Dispose();
}

// ── JSON deserialization models (v2) ────────────────────────────────────────

internal sealed class CdnRegistry
{
    public string? Generated { get; set; }
    public string? Version { get; set; }
    public string? BaseUrl { get; set; }
    public List<CdnArtifact>? Elements { get; set; }
}

internal sealed class CdnArtifact
{
    public string Alias { get; set; } = string.Empty;
    public string Tag   { get; set; } = string.Empty;
    public string Name  { get; set; } = string.Empty;
    public string Tier  { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, string>>? Implementations { get; set; }
}
