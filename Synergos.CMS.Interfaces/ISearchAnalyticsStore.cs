namespace Synergos.CMS.Interfaces;

/// <summary>
/// Captura cada búsqueda ejecutada para análisis posterior (top queries,
/// no-result queries, trends por hora). Aísla a <see cref="ISearchQuery"/>
/// del backend de almacenamiento (in-memory, DB, time-series store).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemSearchAnalyticsStore</c> — JSONL append-only por día
/// en <c>App_Data/syn-search-analytics/</c>, con retención de 90 días. Sustituyó a un almacén
/// en memoria que se perdía en cada reinicio y crecía sin tope.
///
/// <para><b>Esta costura existe para ser reemplazada.</b> El destino previsto es un servicio de
/// sesión propio —fuera del CMS— que respalde búsqueda y demás señales de comportamiento; ese
/// día se registra otro adapter y ni <c>SearchController</c> ni <c>AdminController</c> se
/// enteran, porque hablan solo con esta interfaz.</para>
///
/// <para>Sin async — el record write es fire-and-forget (similar a
/// <see cref="IAnalyticsTracker"/>) y no debe bloquear al usuario. Por lo mismo, una
/// implementación <b>no puede lanzar</b> desde <see cref="Record"/>: una búsqueda que funcionó
/// no puede fallar porque su métrica no se pudo guardar.</para>
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
