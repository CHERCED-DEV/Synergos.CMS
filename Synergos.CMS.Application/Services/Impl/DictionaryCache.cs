using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IDictionaryCache"/>: a thread-safe in-memory
/// store. Population is out of scope for this class — a future
/// Web-layer notification handler invalidates entries when Umbraco
/// publishes dictionary changes; other writers populate via
/// <see cref="Set"/> (not part of the public seam).
/// </summary>
/// <remarks>
/// Per ADR 0009 the contract stays minimal (<see cref="IDictionaryCache.Get"/>
/// + <see cref="IDictionaryCache.Invalidate"/>). <see cref="Set"/> is a
/// class-level helper for populators in the same assembly or tests; it
/// deliberately is NOT part of the <see cref="IDictionaryCache"/>
/// contract so that alternate caches (e.g. distributed) can choose a
/// different population strategy.
/// </remarks>
public sealed class DictionaryCache : IDictionaryCache
{
    private const string CultureSeparator = "::";

    private readonly ConcurrentDictionary<string, string> _store =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string key, string? culture = null) =>
        _store.TryGetValue(Compose(key, culture), out var value) ? value : null;

    public void Invalidate(string? key = null)
    {
        if (key is null)
        {
            _store.Clear();
            return;
        }

        foreach (var storedKey in _store.Keys.Where(k => Matches(k, key)).ToList())
        {
            _store.TryRemove(storedKey, out _);
        }
    }

    /// <summary>
    /// Populates the cache. Not part of the <see cref="IDictionaryCache"/>
    /// contract; reserved for populators in this assembly and for tests.
    /// </summary>
    public void Set(string key, string value, string? culture = null) =>
        _store[Compose(key, culture)] = value;

    private static string Compose(string key, string? culture) =>
        culture is null ? key : culture + CultureSeparator + key;

    private static bool Matches(string storedKey, string key) =>
        storedKey.Equals(key, StringComparison.OrdinalIgnoreCase)
        || storedKey.EndsWith(CultureSeparator + key, StringComparison.OrdinalIgnoreCase);
}
