namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Read-only access to boolean feature toggles. Wraps the typed settings so
/// consumers can ask <c>flags.IsEnabled("EnableBlog")</c> dynamically (e.g.
/// from a middleware) without needing to import the Configuration namespace.
///
/// String-based access is intentional: it keeps the wrapper Domain-pure and
/// allows late-bound checks (admin UI, A/B endpoints, dynamic CSV imports
/// referencing flag names). For compile-time access use the
/// <c>IOptions&lt;FeatureFlagsSettings&gt;</c> directly in Application/Infrastructure.
///
/// Implementations must be thread-safe and side-effect-free.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// Returns the value of the named flag. Unknown names return
    /// <paramref name="defaultIfMissing"/> (default <c>false</c>) — a missing
    /// flag is treated as "feature not enabled" rather than throwing.
    /// </summary>
    bool IsEnabled(string flagName, bool defaultIfMissing = false);

    /// <summary>Snapshot of all known flag names + values for diagnostics.</summary>
    IReadOnlyDictionary<string, bool> Snapshot();
}
