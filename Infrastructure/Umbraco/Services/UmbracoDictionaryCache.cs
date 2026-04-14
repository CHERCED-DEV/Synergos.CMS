using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// IMemoryCache-backed implementation of <see cref="IDictionaryCache"/>.
///
/// Umbraco dictionary items are near-static — they only change when an editor saves
/// or deletes one in the backoffice. Each value is cached individually keyed by
/// "sg_dict::{key}[::{culture}]" with the TTL configured in <c>Synergos:Cache</c>.
///
/// Invalidation strategy: every cache entry is linked to a shared
/// <see cref="CancellationTokenSource"/>. <see cref="Invalidate"/> cancels the token,
/// which atomically expires all entries created by this instance. The token source is
/// then replaced so subsequent reads repopulate.
///
/// The notification handler in <c>Infrastructure/Umbraco/Notifications/DictionaryCacheInvalidator</c>
/// calls <see cref="Invalidate"/> on <c>DictionaryItemSavedNotification</c> +
/// <c>DictionaryItemDeletedNotification</c> so editor changes propagate immediately
/// without waiting for the TTL.
/// </summary>
public sealed class UmbracoDictionaryCache : IDictionaryCache
{
    private const string CacheKeyPrefix = "sg_dict::";

    private readonly ILocalizationService _localization;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UmbracoDictionaryCache> _logger;
    private readonly CacheSettings _cacheSettings;
    private readonly object _resetLock = new();

    // Shared cancellation source linked to every cache entry; cancelling expires them all.
    // Volatile so reads see writes from Invalidate() across threads.
    private volatile CancellationTokenSource _entriesToken = new();

    public UmbracoDictionaryCache(
        ILocalizationService localization,
        IMemoryCache cache,
        ILogger<UmbracoDictionaryCache> logger,
        IOptions<CacheSettings> cacheSettings)
    {
        _localization  = localization;
        _cache         = cache;
        _logger        = logger;
        _cacheSettings = cacheSettings.Value;
    }

    public string Get(string key, string? culture = null, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;

        var cacheKey = BuildCacheKey(key, culture);

        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached ?? fallback;

        var value = Resolve(key, culture);
        if (value is not null)
        {
            CacheValue(cacheKey, value, _cacheSettings.DictionaryMinutes);
            return value;
        }

        // Not found in Umbraco — cache the fallback briefly to avoid hammering ILocalizationService.
        CacheValue(cacheKey, fallback, _cacheSettings.DictionaryMissMinutes);
        _logger.LogDebug("DictionaryCache: key '{Key}' not found for culture '{Culture}'. Using fallback.", key, culture);
        return fallback;
    }

    public IReadOnlyDictionary<string, string> GetAll(string? culture = null)
    {
        var cacheKey = BuildCacheKey("__all__", culture);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
            return cached;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // GetDictionaryItemDescendants(null) returns ALL root items and their descendants.
            foreach (var item in _localization.GetDictionaryItemDescendants(null))
            {
                if (item.ItemKey is null) continue;
                var value = ResolveTranslation(item.Translations, culture);
                if (value is not null)
                    result[item.ItemKey] = value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DictionaryCache: error loading all items.");
        }

        var snapshot = (IReadOnlyDictionary<string, string>)result;
        CacheValue(cacheKey, snapshot, _cacheSettings.DictionaryMinutes);
        return snapshot;
    }

    public IReadOnlyDictionary<string, string> GetMany(IEnumerable<string> keys, string? culture = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var value = Get(key, culture);
            if (!string.IsNullOrEmpty(value))
                result[key] = value;
        }
        return result;
    }

    public void Invalidate()
    {
        // Atomically swap the shared CTS, cancel the old one (which expires every linked
        // cache entry), and dispose it. Subsequent reads will repopulate against the new token.
        CancellationTokenSource old;
        lock (_resetLock)
        {
            old = _entriesToken;
            _entriesToken = new CancellationTokenSource();
        }
        old.Cancel();
        old.Dispose();
        _logger.LogInformation("DictionaryCache: all entries invalidated.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores a value with an absolute TTL and links it to the shared cancellation
    /// token so <see cref="Invalidate"/> can expire it instantly.
    /// </summary>
    private void CacheValue(string cacheKey, object value, int ttlMinutes)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes)
        };
        entryOptions.AddExpirationToken(new CancellationChangeToken(_entriesToken.Token));
        _cache.Set(cacheKey, value, entryOptions);
    }

    private string? Resolve(string key, string? culture)
    {
        try
        {
            var item = _localization.GetDictionaryItemByKey(key);
            return item is null ? null : ResolveTranslation(item.Translations, culture);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DictionaryCache: error resolving key '{Key}'.", key);
            return null;
        }
    }

    private static string? ResolveTranslation(
        IEnumerable<IDictionaryTranslation> translations,
        string? culture)
    {
        if (culture is not null)
        {
            var match = translations.FirstOrDefault(t =>
                string.Equals(t.LanguageIsoCode, culture, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !string.IsNullOrEmpty(match.Value))
                return match.Value;
        }

        return translations.FirstOrDefault(t => !string.IsNullOrEmpty(t.Value))?.Value;
    }

    private static string BuildCacheKey(string key, string? culture)
        => culture is null
            ? $"{CacheKeyPrefix}{key}"
            : $"{CacheKeyPrefix}{key}::{culture}";
}
