using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;

namespace Synergos.CMS.Application.Rendering;

/// <summary>
/// Builds fully-qualified browser URLs for static assets served from
/// the Synergos static-asset origin.
///
/// Single source of truth for all URL construction — no manual concatenation
/// scattered across views, mappers, or helpers.
///
/// Pattern:
///   Base = Origin.TrimEnd('/') + '/' + UiBasePath.Trim('/')
///   Element bundle → Base/{elementName}/{UiFramework}/{UiSlot}/main.js
///   Legacy artifact → Base + '/' + artifact.Path.TrimStart('/')
///   Raw asset       → Origin + '/' + path.TrimStart('/')
///
/// Examples (Origin = "https://static.synergos.local", UiBasePath = "/synergos"):
///   ElementBundle("hero") → "https://static.synergos.local/synergos/hero/angular/latest/main.js"
///   UiBundle("/hero/main.js") → "https://static.synergos.local/synergos/hero/main.js"
///   Asset("/fonts/inter.woff2") → "https://static.synergos.local/fonts/inter.woff2"
///
/// Registered as Singleton. StaticAssetsSettings is IOptions (startup snapshot),
/// which is correct — static-asset origin should not change at runtime.
/// </summary>
public sealed class StaticUrlBuilder
{
    private readonly string _origin;
    private readonly string _uiBase;
    private readonly string _framework;
    private readonly string _slot;

    public StaticUrlBuilder(IOptions<StaticAssetsSettings> options)
    {
        var s      = options.Value;
        _origin    = s.Origin.TrimEnd('/');
        _uiBase    = _origin + "/" + s.UiBasePath.Trim('/');
        _framework = s.UiFramework;
        _slot      = s.UiSlot;
    }

    /// <summary>The static-asset origin (scheme + host, no trailing slash).</summary>
    public string Origin => _origin;

    /// <summary>
    /// Builds a canonical CDN URL for an element bundle.
    /// Path: {UiBasePath}/{elementName}/{UiFramework}/{UiSlot}/main.js
    /// Example: https://static.synergos.local/synergos/hero/angular/latest/main.js
    /// </summary>
    public string ElementBundle(string elementName)
        => $"{_uiBase}/{elementName}/{_framework}/{_slot}/main.js";

    /// <summary>
    /// Builds a URL for a UI element bundle artifact using an explicit relative path.
    /// <paramref name="artifactPath"/> is relative to UiBasePath
    /// (e.g. "hero/main.js"). Prefer <see cref="ElementBundle"/> for standard bundles.
    /// </summary>
    public string UiBundle(string artifactPath)
        => _uiBase + "/" + artifactPath.TrimStart('/');

    /// <summary>
    /// Builds a URL for a raw static asset (image, font, CSS, etc.)
    /// relative to the static origin root.
    /// </summary>
    public string Asset(string path)
        => _origin + "/" + path.TrimStart('/');

    /// <summary>
    /// Builds the CDN path segment for the Angular runtime directory.
    /// Example: "/synergos/runtime/angular/latest"
    /// </summary>
    public string RuntimeBase()
        => $"{_uiBase}/runtime/{_framework}";
}
