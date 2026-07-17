namespace Synergos.CMS.Interfaces;

/// <summary>
/// Captura cada búsqueda ejecutada para análisis posterior (top queries,
/// no-result queries, trends por hora). Aísla a <see cref="ISearchQuery"/>
/// del backend de almacenamiento (in-memory, DB, time-series store).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.InMemorySearchAnalyticsStore</c> —
/// ConcurrentDictionary&lt;query, AggregateRecord&gt;. Para producción
/// con retención larga, swap por adapter sobre TimescaleDB / Influx /
/// CloudWatch.
///
/// Sin async — el record write es fire-and-forget (similar a
/// <see cref="IAnalyticsTracker"/>) y no debe bloquear al usuario.
/// </remarks>
public interface ISearchAnalyticsStore
{
    /// <summary>
    /// Registra una búsqueda ejecutada con su resultado.
    /// </summary>
    void Record(string query, int resultCount, long elapsedMilliseconds);

    /// <summary>
    /// Top N queries (ordenados por count desc) en la ventana indicada.
    /// </summary>
    IReadOnlyList<SearchQueryStat> GetTopQueries(DateTime fromUtc, DateTime toUtc, int limit);

    /// <summary>
    /// Top N queries que devolvieron 0 resultados (ordenados por count
    /// desc) en la ventana. Input directo para el equipo editorial:
    /// "los visitantes buscaron X muchas veces y no encontraron nada —
    /// crear contenido / sinónimos".
    /// </summary>
    IReadOnlyList<SearchQueryStat> GetTopNoResultQueries(DateTime fromUtc, DateTime toUtc, int limit);
}

/// <summary>
/// Snapshot agregado de una query en una ventana temporal.
/// </summary>
/// <param name="Query">El query buscado (lowercase trimmed).</param>
/// <param name="Count">Veces que se buscó.</param>
/// <param name="LastResultCount">Hits del último request (para filtrar
///   "tiene resultados" vs "no tiene").</param>
/// <param name="LastSeenUtc">Última vez que se buscó.</param>
public sealed record SearchQueryStat(
    string Query,
    int Count,
    int LastResultCount,
    DateTime LastSeenUtc);
