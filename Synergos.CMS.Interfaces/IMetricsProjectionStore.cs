namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de proyección de métricas pre-agregadas para el Dashboard
/// (ADR 0097). Acumula eventos en buckets por slug + hora en O(1) de
/// escritura, y devuelve series/totales ya agregados en O(buckets) de
/// lectura — sin re-escanear eventos crudos.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.InMemoryMetricsProjectionStore</c>: buckets
/// en memoria (ConcurrentDictionary) con persistencia JSONL en
/// <c>App_Data/syn-dashboard/projections/</c> — se flushea periódicamente
/// vía <c>DashboardSnapshotFlushHostedService</c> y se rehidrata al boot,
/// por lo que sobrevive un restart.
///
/// <strong>Guardrail</strong>: <see cref="RecordEvent"/> se invoca desde el
/// hot-path de <c>IAnalyticsTracker.Track</c> (vía
/// <c>ProjectingAnalyticsTracker</c>). Debe ser O(1) en memoria, SIN IO
/// síncrono y SIN await. Todo flush a disco vive en el hosted service.
/// </remarks>
public interface IMetricsProjectionStore
{
    /// <summary>
    /// Acumula un evento en su bucket (slug + hora UTC). O(1), in-memory,
    /// sin IO. Eventos con <see cref="MetricEvent.Slug"/> vacío se ignoran.
    /// </summary>
    void RecordEvent(MetricEvent metricEvent);

    /// <summary>
    /// Serie temporal pre-agregada de un slug en el rango [from, to] UTC,
    /// roll-up a la <paramref name="granularity"/> pedida (los buckets se
    /// guardan por hora y se agregan a día si se pide Day). Vacía si no hay
    /// datos.
    /// </summary>
    MetricSeries GetSeries(string slug, DateTime fromUtc, DateTime toUtc, MetricGranularity granularity);

    /// <summary>
    /// Totales (suma + conteo) por slug en el rango [from, to] UTC,
    /// ordenados por conteo desc. Útil para el panel de comportamiento.
    /// </summary>
    IReadOnlyList<MetricTotal> GetTotals(DateTime fromUtc, DateTime toUtc);
}

/// <summary>Granularidad de bucketing de las series del dashboard.</summary>
public enum MetricGranularity
{
    /// <summary>Un punto por hora UTC.</summary>
    Hour,

    /// <summary>Un punto por día UTC (roll-up de las horas).</summary>
    Day,
}

/// <summary>
/// Evento crudo que se proyecta al store. <paramref name="Slug"/> es un
/// nombre de evento estable (ej. <c>"search.executed"</c>,
/// <c>"cart.item-added"</c>, <c>"checkout.completed"</c>).
/// <paramref name="Value"/> es la magnitud a sumar (1 para conteos,
/// el subtotal para revenue, etc.).
/// </summary>
public sealed record MetricEvent(
    string Slug,
    DateTime OccurredUtc,
    decimal Value,
    IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>Serie temporal agregada de un slug, lista para graficar.</summary>
public sealed record MetricSeries(
    string Slug,
    MetricGranularity Granularity,
    IReadOnlyList<MetricDataPoint> Points);

/// <summary>Un punto agregado: bucket UTC + suma + conteo de eventos.</summary>
public sealed record MetricDataPoint(DateTime BucketUtc, decimal Sum, int Count);

/// <summary>Total agregado de un slug en una ventana temporal.</summary>
public sealed record MetricTotal(string Slug, decimal Sum, int Count);
