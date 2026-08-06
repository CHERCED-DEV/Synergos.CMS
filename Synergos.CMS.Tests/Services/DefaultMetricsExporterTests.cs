using System.Globalization;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="DefaultMetricsExporter"/> (ADR 0097 D5). Verifica
/// el formato CSV y que números/fechas sean culture-invariant.
/// </summary>
public sealed class DefaultMetricsExporterTests
{
    private readonly IMetricsProjectionStore _store = Substitute.For<IMetricsProjectionStore>();

    private DefaultMetricsExporter BuildSut() => new(_store);

    [Fact]
    public void ExportSeriesCsv_WritesHeaderAndRows()
    {
        var bucket = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        _store.GetSeries("revenue", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>())
            .Returns(new MetricSeries("revenue", MetricGranularity.Day, new[]
            {
                new MetricDataPoint(bucket, 1234.5m, 3),
            }));

        var csv = BuildSut().ExportSeriesCsv("revenue", bucket.AddDays(-1), bucket.AddDays(1), MetricGranularity.Day);

        Assert.StartsWith("bucketUtc,sum,count", csv);
        Assert.Contains("2026-06-01T10:00:00Z,1234.5,3", csv);
    }

    [Fact]
    public void ExportSeriesCsv_CultureInvariant_DecimalPoint()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-CO");   // coma decimal
            var bucket = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            _store.GetSeries(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>())
                .Returns(new MetricSeries("x", MetricGranularity.Day, new[] { new MetricDataPoint(bucket, 0.7629m, 1) }));

            var csv = BuildSut().ExportSeriesCsv("x", bucket, bucket, MetricGranularity.Day);

            Assert.Contains("0.7629", csv);
            Assert.DoesNotContain("0,7629", csv);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void ExportTotalsCsv_WritesHeaderAndRows()
    {
        _store.GetTotals(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            new MetricTotal("cart.item-added", 0m, 5),
        });

        var csv = BuildSut().ExportTotalsCsv(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        Assert.StartsWith("slug,sum,count", csv);
        Assert.Contains("cart.item-added,0,5", csv);
    }

    [Fact]
    public void ExportTotalsCsv_Empty_OnlyHeader()
    {
        _store.GetTotals(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<MetricTotal>());

        var csv = BuildSut().ExportTotalsCsv(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        Assert.Equal("slug,sum,count\r\n", csv);
    }
}
