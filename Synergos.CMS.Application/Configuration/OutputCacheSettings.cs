namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:OutputCache</c>. Gobierna los TTL de los endpoints
/// operacionales que cachean su rendered output para reducir CPU bajo
/// crawler load (sitemap, RSS, robots no porque es trivial generar).
/// </summary>
/// <remarks>
/// Cache es in-memory (IMemoryCache) — se invalida con restart del
/// proceso. NO se sincroniza entre instancias bajo load balancer; cada
/// instancia mantiene su copia. Aceptable para los outputs cachados
/// (no son críticos en la consistencia entre instancias — un crawler
/// recibe la versión del nodo que le respondió).
/// </remarks>
public sealed class OutputCacheSettings
{
    /// <summary>
    /// TTL del sitemap.xml. Default 60 minutos. Crawlers consultan
    /// sitemap rara vez (1-2 veces/día); 60 min es agresivo pero
    /// asegura que cambios de schema se reflejen en horas.
    /// </summary>
    public int SitemapMinutes { get; init; } = 60;

    /// <summary>
    /// TTL del blog RSS. Default 30 minutos. Feed readers consultan
    /// RSS más frecuente (cada 15-60 min); 30 min balancea freshness
    /// con load.
    /// </summary>
    public int BlogRssMinutes { get; init; } = 30;

    /// <summary>
    /// TTL del news-sitemap.xml. Default 15 minutos. Google News re-
    /// crawlea más frecuente que sitemap regular y solo nos importan
    /// los posts de las últimas 48h — TTL bajo asegura freshness.
    /// </summary>
    public int NewsSitemapMinutes { get; init; } = 15;

    /// <summary>
    /// Ventana en horas para incluir un postPage en news-sitemap.xml.
    /// Default 48 conforme al Google News Sitemap Protocol — posts más
    /// viejos no se listan (Google ignora entries con publication_date
    /// más vieja que ~2 días en este endpoint).
    /// </summary>
    public int NewsSitemapWindowHours { get; init; } = 48;

    /// <summary>
    /// Si true, cache deshabilitado completamente (cada request
    /// regenera). Default false. Útil para debugging o sitios con
    /// requirements de freshness inmediata.
    /// </summary>
    public bool Disabled { get; init; } = false;
}
