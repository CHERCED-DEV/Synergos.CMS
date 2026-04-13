using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Presentation.ViewModels.Layout;

namespace Synergos.CMS.Presentation.ViewComponents;

public sealed class LayoutHeadViewComponent : ViewComponent
{
    private readonly ISiteResolver        _siteResolver;
    private readonly IThemeService        _themeService;
    private readonly ITrackingService     _trackingService;
    private readonly IArtifactResolver     _artifactResolver;
    private readonly StaticAssetsSettings _staticSettings;

    public LayoutHeadViewComponent(
        ISiteResolver                  siteResolver,
        IThemeService                  themeService,
        ITrackingService               trackingService,
        IArtifactResolver               artifactResolver,
        IOptions<StaticAssetsSettings> staticOptions)
    {
        _siteResolver    = siteResolver;
        _themeService    = themeService;
        _trackingService = trackingService;
        _artifactResolver = artifactResolver;
        _staticSettings  = staticOptions.Value;
    }

    public Task<IViewComponentResult> InvokeAsync()
    {
        var site    = _siteResolver.Resolve();
        var layout  = _themeService.GetLayoutConfig(site.RootNodeId);
        var scripts = _trackingService.GetTrackingScripts(site.RootNodeId);
        var trackingScripts = scripts.HeadScripts;

        var cdnStyles = _artifactResolver.GetClientStyleUrls().ToList();
        var importMapJson = LoadImportMapJson();

        var model = new LayoutHeadViewModel(
            ThemeColor: layout.ThemeVariant,
            TrackingHeadScriptsHtml: trackingScripts,
            CdnStyleUrls: cdnStyles,
            ImportMapJson: importMapJson);

        return Task.FromResult<IViewComponentResult>(View(model));
    }

    /// <summary>
    /// Reads the import-map.json from the local CDN directory.
    /// Falls back to the "latest" folder if the versioned folder is not found.
    /// Returns null if the file does not exist (elements will fall back to SSR).
    /// Normalizes integrity keys from bare specifiers to full URLs so the browser
    /// does not emit "Integrity key is not a valid URL" warnings.
    /// </summary>
    private string? LoadImportMapJson()
    {
        var basePath  = _staticSettings.RegistryLocalPath;
        var uiBase    = _staticSettings.UiBasePath.Trim('/');
        var framework = _staticSettings.UiFramework;

        // Try "latest" slot first (most common for local dev)
        var latestPath = Path.Combine(basePath, uiBase, "runtime", framework, "latest", "import-map.json");
        if (File.Exists(latestPath))
            return NormalizeImportMap(File.ReadAllText(latestPath));

        // Try versioned folders — pick the highest
        var runtimeDir = Path.Combine(basePath, uiBase, "runtime", framework);
        if (Directory.Exists(runtimeDir))
        {
            var versionedFile = Directory.GetDirectories(runtimeDir)
                .Select(d => Path.Combine(d, "import-map.json"))
                .Where(File.Exists)
                .OrderDescending()
                .FirstOrDefault();

            if (versionedFile is not null)
                return NormalizeImportMap(File.ReadAllText(versionedFile));
        }

        return null;
    }

    /// <summary>
    /// Ensures every key in the import map's <c>integrity</c> block is a URL
    /// (absolute or starting with '/', './', '../') rather than a bare specifier
    /// like "ng-core.js". Maps each bare filename to the matching URL already
    /// declared in the <c>imports</c> block.
    ///
    /// Also substitutes the <c>__BASE_URL__</c> placeholder with the configured
    /// static-assets origin so the map is valid before it reaches the browser.
    /// </summary>
    private string NormalizeImportMap(string json)
    {
        try
        {
            var origin = _staticSettings.Origin.TrimEnd('/');
            json = json.Replace("__BASE_URL__", origin, StringComparison.Ordinal);

            var root = JsonNode.Parse(json);
            if (root is null) return json;

            var importsNode   = root["imports"]?.AsObject();
            var integrityNode = root["integrity"]?.AsObject();

            if (importsNode is null || integrityNode is null) return json;

            // Build filename → resolved URL from the imports section
            var fileToUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, value) in importsNode)
            {
                var url = value?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrEmpty(url)) continue;
                var filename = Path.GetFileName(url.Split('?')[0]);
                if (!string.IsNullOrEmpty(filename))
                    fileToUrl.TryAdd(filename, url);
            }

            // Check if any key is still a bare specifier
            var hasBareKeys = integrityNode.Any(p =>
                !p.Key.StartsWith('/') &&
                !p.Key.StartsWith("./") &&
                !p.Key.StartsWith("../") &&
                !p.Key.StartsWith("http", StringComparison.OrdinalIgnoreCase));

            if (!hasBareKeys) return root.ToJsonString();

            // Rebuild integrity section with URL keys
            var normalized = new JsonObject();
            foreach (var (key, value) in integrityNode)
            {
                var hash = value?.GetValue<string>() ?? string.Empty;
                var isUrl = key.StartsWith('/') || key.StartsWith("./") ||
                            key.StartsWith("../") || key.StartsWith("http", StringComparison.OrdinalIgnoreCase);

                var newKey = isUrl ? key
                    : fileToUrl.TryGetValue(key, out var mapped) ? mapped : key;

                normalized[newKey] = JsonValue.Create(hash);
            }

            root["integrity"] = normalized;
            return root.ToJsonString();
        }
        catch
        {
            // If anything fails, serve the original — no integrity checking is better
            // than a broken import map.
            return json;
        }
    }
}
