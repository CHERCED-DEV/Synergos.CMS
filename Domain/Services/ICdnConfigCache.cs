namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Per-request memo of serialized CDN element configurations keyed by their
/// content hash. When the same element appears N times on a page (e.g. 10
/// product cards in a listing) the JSON serialization runs only once.
/// Inspired by <c>BlockHandler.GetModuleData</c> in NS.Booking.CMS.
///
/// Lifetime: scoped (one cache per request, GC'd at end). The hash is a
/// stable function of the config's bytes — collisions are statistically
/// negligible at xxHash64 / FNV-64 strength.
///
/// Implementations must be thread-safe within the scope (parallel async
/// partials may render concurrently).
/// </summary>
public interface ICdnConfigCache
{
    /// <summary>
    /// Returns a cached serialization for <paramref name="config"/> if its
    /// hash was seen earlier in this request, otherwise invokes <paramref
    /// name="serializer"/>, caches the result, and returns it.
    /// </summary>
    string GetOrAdd<TConfig>(TConfig config, Func<TConfig, string> serializer)
        where TConfig : notnull;

    /// <summary>Cardinality (distinct configs serialized in this request) — diagnostics.</summary>
    int Count { get; }
}
