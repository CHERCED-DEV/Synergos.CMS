namespace Synergos.CMS.Configuration;

/// <summary>
/// Boolean feature toggles bound from <c>Synergos:FeatureFlags</c>.
/// Inspired by NS.Booking.CMS's <c>FeatureFlagsConfiguration.json</c> —
/// flat dictionary, no LaunchDarkly, no middleware ceremony, just a typed
/// settings record + an <see cref="Domain.Services.IFeatureFlags"/> service
/// that reads it.
///
/// Add a flag: declare a property here, set its default, expose it via the
/// service. Editors/ops flip it via <c>appsettings.json</c> per environment.
///
/// Example:
/// <code>
/// "Synergos": {
///   "FeatureFlags": {
///     "EnableBlog": true,
///     "EnableShop": false,
///     "EnableExperimentalAreaContainers": true
///   }
/// }
/// </code>
/// </summary>
public sealed class FeatureFlagsSettings
{
    public const string SectionPath = "Synergos:FeatureFlags";

    /// <summary>Whether the blog surface (BlogHome/BlogPost/Category/Author) is publicly routable.</summary>
    public bool EnableBlog { get; init; } = true;

    /// <summary>Whether shop pages route at all (when false, controllers return 404).</summary>
    public bool EnableShop { get; init; } = false;

    /// <summary>Whether form submissions accept new entries (vs. read-only).</summary>
    public bool EnableForms { get; init; } = true;

    /// <summary>Emit <c>&lt;link rel="modulepreload"&gt;</c> hints from BundlePrescanner.</summary>
    public bool EnablePreloadHints { get; init; } = true;

    /// <summary>Hash-based dedup of CDN config serializations within a request.</summary>
    public bool EnableCdnConfigCache { get; init; } = true;

    /// <summary>Allow newer area-based container patterns (CardGrid, LogoCloudGrid).</summary>
    public bool EnableExperimentalAreaContainers { get; init; } = true;
}
