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
/// <para><b><see cref="Record"/> sigue sin async</b>: es fire-and-forget (similar a
/// <see cref="IAnalyticsTracker"/>) y no debe bloquear al usuario. Por lo mismo, una
/// implementación <b>no puede lanzar</b> desde ahí: una búsqueda que funcionó no puede fallar
/// porque su métrica no se pudo guardar.</para>
///
/// <para><b>Las LECTURAS sí son async</b>, desde que existe un adapter que las sirve por red
/// (ADR 0130). No es simetría estética: bloquear un hilo del pool esperando a otro proceso es
/// lo que agota el pool bajo carga, y <c>/api/search/analytics</c> es
/// <c>[AllowAnonymous]</c> con un gate por rol que sale de configuración —hoy
/// <c>"admin,editor"</c>, pero vaciarlo es una línea— así que el peor caso es una ruta pública
/// con una llamada de red bloqueante por request. La implementación de fichero devuelve tareas
/// ya completadas y no paga nada por esto.</para>
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
    Task<IReadOnlyList<SearchQueryStat>> GetTopQueriesAsync(
        DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Top N queries que devolvieron 0 resultados (ordenados por count
    /// desc) en la ventana. Input directo para el equipo editorial:
    /// "los visitantes buscaron X muchas veces y no encontraron nada —
    /// crear contenido / sinónimos".
    /// </summary>
    Task<IReadOnlyList<SearchQueryStat>> GetTopNoResultQueriesAsync(
        DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken = default);
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
