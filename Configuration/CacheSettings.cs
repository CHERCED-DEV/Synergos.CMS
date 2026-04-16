namespace Synergos.CMS.Configuration;

/// <summary>
/// Strongly-typed cache configuration.
/// Bound from appsettings.json → Synergos:Cache
/// </summary>
public sealed class CacheSettings
{
    public const string SectionPath = "Synergos:Cache";

    /// <summary>
    /// Dictionary item TTL in minutes.
    /// Umbraco dictionary entries are near-static — default 10 minutes.
    /// Lower values increase backoffice responsiveness; higher values reduce DB load.
    /// </summary>
    public int DictionaryMinutes { get; init; } = 10;

    /// <summary>
    /// TTL for negative cache entries (key not found in Umbraco).
    /// Short by design: avoids hammering ILocalizationService without causing
    /// stale "key not found" states during deployments.
    /// </summary>
    public int DictionaryMissMinutes { get; init; } = 1;

    /// <summary>
    /// Default culture used as deterministic fallback when a dictionary key
    /// has no translation for the requested culture. Inspired by NS.Booking.CMS:
    /// instead of "first available translation" (non-deterministic), fall back
    /// to a configured default. Empty = legacy behavior (first-available).
    /// </summary>
    public string DefaultCulture { get; init; } = "es-CO";
}
