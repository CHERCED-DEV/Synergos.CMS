using System.Globalization;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMetricsExporter"/> (ADR 0097 D5). Formatea series y
/// totales de <see cref="IMetricsProjectionStore"/> a CSV culture-invariant.
/// </summary>
public sealed class DefaultMetricsExporter : IMetricsExporter
{
    private readonly IMetricsProjectionStore _store;

    public DefaultMetricsExporter(IMetricsProjectionStore store) => _store = store;

    public string ExportSeriesCsv(string slug, DateTime fromUtc, DateTime toUtc, MetricGranularity granularity)
    {
        var series = _store.GetSeries(slug, fromUtc, toUtc, granularity);
        var sb = new StringBuilder();
        sb.Append("bucketUtc,sum,count\r\n");
        foreach (var p in series.Points)
        {
            sb.Append(p.BucketUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.Sum.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public string ExportTotalsCsv(DateTime fromUtc, DateTime toUtc)
    {
        var totals = _store.GetTotals(fromUtc, toUtc);
        var sb = new StringBuilder();
        sb.Append("slug,sum,count\r\n");
        foreach (var t in totals)
        {
            sb.Append(Csv(t.Slug));
            sb.Append(',');
            sb.Append(t.Sum.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(t.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    // Quoting CSV mínimo por si un slug trajera coma/comilla/salto.
    private static string Csv(string field) =>
        field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
}
