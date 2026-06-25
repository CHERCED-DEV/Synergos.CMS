using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="InMemoryMetricsProjectionStore"/> (ADR 0097).
/// Cubre empty / happy / filter / roll-up día / flush+rehidratación /
/// retención, con aislamiento por temp directory.
/// </summary>
public sealed class InMemoryMetricsProjectionStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;

    public InMemoryMetricsProjectionStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-metrics-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private InMemoryMetricsProjectionStore BuildSut(int retentionDays = 730) =>
        new(_env,
            Options.Create(new DashboardSettings { ProjectionRetentionDays = retentionDays }),
            NullLogger<InMemoryMetricsProjectionStore>.Instance);

    [Fact]
    public void GetSeries_EmptyStore_ReturnsEmpty()
    {
        var sut = BuildSut();
        var series = sut.GetSeries("search.executed", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, MetricGranularity.Hour);
        Assert.Empty(series.Points);
        Assert.Equal("search.executed", series.Slug);
    }

    [Fact]
    public void GetTotals_EmptyStore_ReturnsEmpty()
    {
        var sut = BuildSut();
        Assert.Empty(sut.GetTotals(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
    }

    [Fact]
    public void RecordEvent_EmptySlug_Ignored()
    {
        var sut = BuildSut();
        sut.RecordEvent(new MetricEvent("", DateTime.UtcNow, 1m));
        sut.RecordEvent(new MetricEvent("   ", DateTime.UtcNow, 1m));
        Assert.Empty(sut.GetTotals(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void RecordEvent_AccumulatesSumAndCount_SameHour()
    {
        var sut = BuildSut();
        var t = new DateTime(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc);
        sut.RecordEvent(new MetricEvent("checkout.completed", t, 10m));
        sut.RecordEvent(new MetricEvent("checkout.completed", t.AddMinutes(20), 5m));

        var series = sut.GetSeries("checkout.completed", t.AddHours(-1), t.AddHours(1), MetricGranularity.Hour);
        var point = Assert.Single(series.Points);
        Assert.Equal(15m, point.Sum);
        Assert.Equal(2, point.Count);
        Assert.Equal(new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), point.BucketUtc);
    }

    [Fact]
    public void GetSeries_Day_RollsUpHours()
    {
        var sut = BuildSut();
        var t = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        sut.RecordEvent(new MetricEvent("cart.item-added", t, 1m));
        sut.RecordEvent(new MetricEvent("cart.item-added", t.AddHours(3), 1m));

        var hourly = sut.GetSeries("cart.item-added", t.AddHours(-1), t.AddHours(5), MetricGranularity.Hour);
        Assert.Equal(2, hourly.Points.Count);

        var daily = sut.GetSeries("cart.item-added", t.AddHours(-1), t.AddHours(5), MetricGranularity.Day);
        var dayPoint = Assert.Single(daily.Points);
        Assert.Equal(2, dayPoint.Count);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), dayPoint.BucketUtc);
    }

    [Fact]
    public void GetSeries_FiltersByDateRange()
    {
        var sut = BuildSut();
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        sut.RecordEvent(new MetricEvent("form.submitted", now.AddDays(-3), 1m));
        sut.RecordEvent(new MetricEvent("form.submitted", now, 1m));

        var series = sut.GetSeries("form.submitted", now.AddDays(-1), now.AddHours(1), MetricGranularity.Hour);
        var point = Assert.Single(series.Points);
        Assert.Equal(now, point.BucketUtc);
    }

    [Fact]
    public void GetTotals_OrdersByCountDesc()
    {
        var sut = BuildSut();
        var now = DateTime.UtcNow;
        sut.RecordEvent(new MetricEvent("a", now, 1m));
        sut.RecordEvent(new MetricEvent("a", now, 1m));
        sut.RecordEvent(new MetricEvent("a", now, 1m));
        sut.RecordEvent(new MetricEvent("b", now, 1m));

        var totals = sut.GetTotals(now.AddHours(-1), now.AddHours(1));
        Assert.Equal(2, totals.Count);
        Assert.Equal("a", totals[0].Slug);
        Assert.Equal(3, totals[0].Count);
        Assert.Equal("b", totals[1].Slug);
    }

    [Fact]
    public async Task Flush_Then_NewStore_Rehydrates()
    {
        var sut = BuildSut();
        var t = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        sut.RecordEvent(new MetricEvent("search.executed", t, 1m));
        sut.RecordEvent(new MetricEvent("search.executed", t, 1m));
        await sut.FlushAsync(CancellationToken.None);

        var reborn = BuildSut();   // mismo temp root → rehidrata en el ctor
        var totals = reborn.GetTotals(t.AddHours(-1), t.AddHours(1));
        var total = Assert.Single(totals);
        Assert.Equal("search.executed", total.Slug);
        Assert.Equal(2, total.Count);
    }

    [Fact]
    public async Task Flush_AppliesRetention_DropsOldBuckets()
    {
        var sut = BuildSut(retentionDays: 1);
        sut.RecordEvent(new MetricEvent("old.event", DateTime.UtcNow.AddDays(-5), 1m));
        sut.RecordEvent(new MetricEvent("recent.event", DateTime.UtcNow, 1m));
        await sut.FlushAsync(CancellationToken.None);

        var reborn = BuildSut(retentionDays: 1);
        var totals = reborn.GetTotals(DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(1));
        var total = Assert.Single(totals);
        Assert.Equal("recent.event", total.Slug);
    }

    [Fact]
    public void GetSeries_Day_SeparatesAcrossDayBoundary()
    {
        var sut = BuildSut(retentionDays: 0);
        var lateNight = new DateTime(2026, 6, 1, 23, 0, 0, DateTimeKind.Utc);
        var earlyNext = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        sut.RecordEvent(new MetricEvent("e", lateNight, 1m));
        sut.RecordEvent(new MetricEvent("e", earlyNext, 1m));

        var daily = sut.GetSeries("e", lateNight.AddHours(-1), earlyNext.AddHours(1), MetricGranularity.Day);
        Assert.Equal(2, daily.Points.Count);   // distintos días → NO se fusionan
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), daily.Points[0].BucketUtc);
        Assert.Equal(new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), daily.Points[1].BucketUtc);
        Assert.All(daily.Points, p => Assert.Equal(1, p.Count));
    }

    [Fact]
    public void Rehydrate_CorruptedLines_SkipsSilently()
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-dashboard", "projections");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "projection.jsonl"), new[]
        {
            "{\"Slug\":\"valid.event\",\"BucketUtc\":\"2026-06-01T10:00:00Z\",\"Sum\":3,\"Count\":3}",
            "esto no es json valido",
            "",
            "{ \"partial\":",
        });

        var sut = BuildSut(retentionDays: 0);   // rehidrata en el ctor, ignora líneas inválidas
        var t = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var totals = sut.GetTotals(t.AddHours(-1), t.AddHours(1));
        var total = Assert.Single(totals);
        Assert.Equal("valid.event", total.Slug);
        Assert.Equal(3, total.Count);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
