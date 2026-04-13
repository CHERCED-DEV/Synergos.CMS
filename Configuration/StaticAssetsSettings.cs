namespace Synergos.CMS.Configuration;

/// <summary>
/// Origin and local-path settings for the Synergos static-asset server.
/// Bound from appsettings.json section "Synergos:StaticAssets".
///
/// Design: separates the browser-visible URL (<c>Origin</c>) from the
/// server-side filesystem location (<c>RegistryLocalPath</c>) used only
/// at startup to load the artifact registry. This means Origin can point
/// to a CDN, an IIS site, or a dev server without the code caring.
///
/// Environments:
///   Development → Origin = "https://static.synergos.local"
///   QA          → Origin = "https://static-qa.synergos.app"
///   Production  → Origin = "https://static.synergos.app"
///
/// No code changes are required when switching environments — only
/// the configuration section changes.
/// </summary>
public sealed class StaticAssetsSettings
{
    public const string SectionPath = "Synergos:StaticAssets";

    /// <summary>
    /// Base URL of the static-asset server as seen by the browser.
    /// Scheme + host, no trailing slash.
    /// Example: "https://static.synergos.local"
    /// </summary>
    public string Origin { get; init; } = string.Empty;

    /// <summary>
    /// Base CDN path appended to Origin when building UI bundle URLs.
    /// Example: "/synergos" → Origin + "/synergos/{element}/{framework}/{slot}/main.js"
    /// Kept separate from Origin so Origin can be used as-is for CORS policy.
    /// </summary>
    public string UiBasePath { get; init; } = "/synergos";

    /// <summary>
    /// Framework name used to build CDN element bundle paths.
    /// Default: "angular"
    /// </summary>
    public string UiFramework { get; init; } = "angular";

    /// <summary>
    /// CDN version slot for element bundles.
    /// Options: "latest" (dev/staging), "v{N}" (production pinned), exact semver.
    /// Default: "latest"
    /// </summary>
    public string UiSlot { get; init; } = "latest";

    /// <summary>
    /// Filesystem path to the root of the local static-asset server.
    /// Used server-side only by ArtifactResolver to locate registry.json at startup.
    /// Example: "C:\\LOCAL_CDN"
    /// </summary>
    public string RegistryLocalPath { get; init; } = string.Empty;

    /// <summary>
    /// Relative path to the artifact registry manifest inside RegistryLocalPath.
    /// Default: "/synergos/registry.json"
    /// </summary>
    public string RegistryManifest { get; init; } = "/synergos/registry.json";

    /// <summary>
    /// Element type aliases rendered as client-side Custom Elements instead of
    /// SSR Razor partials. Add an alias to activate client-hosted rendering;
    /// remove it to fall back to SSR.
    /// </summary>
    public string[] ClientHosted { get; init; } = [];
}
