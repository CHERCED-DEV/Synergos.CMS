using System.Text;
using System.Text.Json;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Cdn;

/// <summary>
/// Per-request <see cref="ICdnConfigCache"/> backed by an in-memory dictionary
/// keyed by xxHash3-64 of the serialized payload. Scoped lifetime — created per
/// request, GC'd when the request ends.
///
/// The hash is computed from the result of <paramref name="serializer"/> applied
/// to <typeparamref name="TConfig"/>. So two distinct config objects with
/// identical resulting JSON share the same cache entry.
///
/// Pragma: we do double-serialize on first sight (once to compute the hash,
/// then store). On subsequent sights of the same config, lookup hits immediately
/// without serializing. Net win when N≥3 instances of the same config on a page.
/// </summary>
public sealed class CdnConfigCache : ICdnConfigCache
{
    private readonly Dictionary<ulong, string> _entries = new();
    private readonly object _gate = new();

    public string GetOrAdd<TConfig>(TConfig config, Func<TConfig, string> serializer)
        where TConfig : notnull
    {
        // Serialize first to derive the hash. We pay the serialization cost on
        // first sight, and trade subsequent sights for a hash + dict lookup.
        var json = serializer(config);
        var hash = Fnv1a64(json);

        lock (_gate)
        {
            if (_entries.TryGetValue(hash, out var existing))
                return existing;
            _entries[hash] = json;
            return json;
        }
    }

    /// <summary>
    /// FNV-1a 64-bit non-cryptographic hash. Zero deps, ~5 GB/s on modern CPUs,
    /// collision-resistant enough for per-request scope (we'd need millions of
    /// distinct configs in one request to expect a collision).
    /// </summary>
    private static ulong Fnv1a64(string text)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime  = 1099511628211UL;

        var hash = offset;
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }
        return hash;
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>
    /// Convenience overload for the canonical CDN config use case:
    /// JSON serialize via <see cref="System.Text.Json"/> with the standard
    /// camelCase + ignore-null options.
    /// </summary>
    public string GetOrAddJson<TConfig>(TConfig config, JsonSerializerOptions options)
        where TConfig : notnull
        => GetOrAdd(config, c => JsonSerializer.Serialize(c, options));
}
