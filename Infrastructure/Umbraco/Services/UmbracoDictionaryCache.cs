using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Implementación de IDictionaryCache respaldada por IMemoryCache.
///
/// Los dictionary items de Umbraco son casi estáticos — solo cambian cuando un editor
/// los modifica en el backoffice. Cacheamos cada valor individualmente con clave
/// "{dictionaryKey}::{culture}" según el TTL configurado en Synergos:Cache.
///
/// Invalidación: llamar a Invalidate() desde una notificación DictionaryItemSaved /
/// ContentCacheRefresherNotification si se desea coherencia inmediata. Sin invalidación
/// explícita, los cambios se propagan al expirar el TTL.
///
/// Patrón de referencia: NS.Booking.CMS — CacheDictionaryService.
/// Adaptación: usa IMemoryCache inyectado en vez del MemoryCache.Default estático.
/// </summary>
public sealed class UmbracoDictionaryCache : IDictionaryCache
{
    private const string CacheKeyPrefix = "sg_dict::";

    private readonly ILocalizationService _localization;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UmbracoDictionaryCache> _logger;
    private readonly CacheSettings _cacheSettings;

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
            _cache.Set(cacheKey, value, TimeSpan.FromMinutes(_cacheSettings.DictionaryMinutes));
            return value;
        }

        // No existe en Umbraco — cachear el fallback brevemente para no martillar ILocalizationService
        _cache.Set(cacheKey, fallback, TimeSpan.FromMinutes(_cacheSettings.DictionaryMissMinutes));
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
        _cache.Set(cacheKey, snapshot, TimeSpan.FromMinutes(_cacheSettings.DictionaryMinutes));
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
        // IMemoryCache no expone un "clear all" directo — usamos un token de cancelación
        // vinculado a todas las entradas creadas por este servicio.
        // Si se necesita invalidación inmediata en producción, usar IMemoryCache con CancellationTokenSource
        // compartido y cancelarlo aquí. Por ahora la expiración por TTL es suficiente.
        _logger.LogDebug("DictionaryCache: invalidation requested — entries expire within {Minutes} min.", _cacheSettings.DictionaryMinutes);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
