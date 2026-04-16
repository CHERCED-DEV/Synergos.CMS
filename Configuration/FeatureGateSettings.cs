namespace Synergos.CMS.Configuration;

/// <summary>
/// Declarative route gates bound from <c>Synergos:FeatureGates</c>.
///
/// Each gate maps a URL path prefix to a <see cref="FeatureFlagsSettings"/>
/// property name. Requests whose path starts with the prefix are short-circuited
/// to 404 when the named flag is <c>false</c> — gating entire route trees
/// without requiring every controller to check the flag manually.
///
/// Controllers that need fine-grained logic (e.g. different behaviour when a
/// flag is off, vs. hard 404) should keep their own <c>IFeatureFlags</c> check.
/// Middleware is for uniform "all routes under /blog require EnableBlog".
///
/// Example:
/// <code>
/// "Synergos": {
///   "FeatureGates": {
///     "Gates": [
///       { "PathPrefix": "/blog",     "FeatureFlag": "EnableBlog" },
///       { "PathPrefix": "/shop",     "FeatureFlag": "EnableShop" },
///       { "PathPrefix": "/forms",    "FeatureFlag": "EnableForms" }
///     ]
///   }
/// }
/// </code>
/// </summary>
public sealed class FeatureGateSettings
{
    public const string SectionPath = "Synergos:FeatureGates";

    public IReadOnlyList<FeatureGate> Gates { get; init; } = [];
}

/// <summary>A single route gate: path prefix → feature flag.</summary>
public sealed class FeatureGate
{
    /// <summary>URL path prefix (case-insensitive). Must start with <c>/</c>.</summary>
    public string PathPrefix { get; init; } = string.Empty;

    /// <summary>Name of the <see cref="FeatureFlagsSettings"/> boolean property.</summary>
    public string FeatureFlag { get; init; } = string.Empty;
}
