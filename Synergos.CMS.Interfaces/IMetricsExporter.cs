namespace Synergos.CMS.Interfaces;

/// <summary>
/// Exporta métricas del Dashboard a CSV (ADR 0097 D5). Lo consume el botón
/// "Exportar" del panel vía <c>GET /api/dashboard/export</c>.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultMetricsExporter</c> y lee de
/// <see cref="IMetricsProjectionStore"/>. Números y fechas se formatean
/// culture-invariant (punto decimal, ISO-8601 UTC) para que cualquier hoja
/// de cálculo los parsee igual sin importar el locale del host.
/// </remarks>
public interface IMetricsExporter
{
    /// <summary>
    /// CSV de la serie temporal de un slug: columnas <c>bucketUtc,sum,count</c>.
    /// </summary>
    string ExportSeriesCsv(string slug, DateTime fromUtc, DateTime toUtc, MetricGranularity granularity);

    /// <summary>
    /// CSV de los totales por slug en la ventana: columnas <c>slug,sum,count</c>.
    /// </summary>
    string ExportTotalsCsv(DateTime fromUtc, DateTime toUtc);
}
