using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMetricsProjectionStore"/> (ADR 0097). Buckets en
/// memoria por slug + hora UTC (escritura O(1), lock por-slug, cero IO),
/// con persistencia JSONL en <c>App_Data/syn-dashboard/projections/projection.jsonl</c>.
/// El flush periódico lo dispara <c>DashboardSnapshotFlushHostedService</c>;
/// al construirse rehidrata desde disco, por lo que sobrevive un restart.
/// </summary>
/// <remarks>
/// Single-instance por diseño (ADR 0097 D1, sin partición por siteRootKey).
/// Para multi-instancia load-balanced, swap por adapter sobre Redis/SQL con
/// lock distribuido. La escritura a disco es atómica (temp + <c>File.Move</c>).
/// </remarks>
public sealed class InMemoryMetricsProjectionStore : IMetricsProjectionStore
{
    private const string DirectorySegment1 = "syn-dashboard";
    private const string DirectorySegment2 = "projections";
    private const string FileName = "projection.jsonl";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptions<DashboardSettings> _settings;
    private readonly ILogger<InMemoryMetricsProjectionStore> _logger;

    // slug -> (hourBucketUtc -> acumulador). Lock por SlugBuckets para
    // acumular sin contención global.
    private readonly ConcurrentDictionary<string, SlugBuckets> _buckets = new(StringComparer.Ordinal);
    private readonly object _flushLock = new();

    public InMemoryMetricsProjectionStore(
        IHostEnvironment hostEnvironment,
        IOptions<DashboardSettings> settings,
        ILogger<InMemoryMetricsProjectionStore> logger)
    {
        _hostEnvironment = hostEnvironment;
        _settings = settings;
        _logger = logger;
        Rehydrate();
    }

    public void RecordEvent(MetricEvent metricEvent)
    {
        if (metricEvent is null || string.IsNullOrWhiteSpace(metricEvent.Slug))
        {
            return;
        }

        var bucketUtc = FloorToHour(metricEvent.OccurredUtc);
        var slugBuckets = _buckets.GetOrAdd(metricEvent.Slug, static _ => new SlugBuckets());
        lock (slugBuckets.Gate)
        {
            if (!slugBuckets.ByHour.TryGetValue(bucketUtc, out var acc))
            {
                acc = new Accumulator();
                slugBuckets.ByHour[bucketUtc] = acc;
            }
            acc.Sum += metricEvent.Value;
            acc.Count++;
        }
    }

    public MetricSeries GetSeries(string slug, DateTime fromUtc, DateTime toUtc, MetricGranularity granularity)
    {
        if (string.IsNullOrWhiteSpace(slug) || !_buckets.TryGetValue(slug, out var slugBuckets))
        {
            return new MetricSeries(slug ?? string.Empty, granularity, Array.Empty<MetricDataPoint>());
        }

        var from = FloorToHour(fromUtc);
        var to = toUtc;
        List<KeyValuePair<DateTime, Accumulator>> snapshot;
        lock (slugBuckets.Gate)
        {
            snapshot = slugBuckets.ByHour
                .Where(kv => kv.Key >= from && kv.Key <= to)
                .Select(kv => new KeyValuePair<DateTime, Accumulator>(kv.Key, kv.Value.Copy()))
                .ToList();
        }

        IEnumerable<MetricDataPoint> points = granularity == MetricGranularity.Day
            ? snapshot
                .GroupBy(kv => kv.Key.Date)
                .Select(g => new MetricDataPoint(
                    DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                    g.Sum(x => x.Value.Sum),
                    g.Sum(x => x.Value.Count)))
            : snapshot
                .Select(kv => new MetricDataPoint(kv.Key, kv.Value.Sum, kv.Value.Count));

        return new MetricSeries(slug, granularity, points.OrderBy(p => p.BucketUtc).ToList());
    }

    public IReadOnlyList<MetricTotal> GetTotals(DateTime fromUtc, DateTime toUtc)
    {
        var from = FloorToHour(fromUtc);
        var totals = new List<MetricTotal>();
        foreach (var (slug, slugBuckets) in _buckets)
        {
            decimal sum = 0m;
            var count = 0;
            lock (slugBuckets.Gate)
            {
                foreach (var (bucketUtc, acc) in slugBuckets.ByHour)
                {
                    if (bucketUtc < from || bucketUtc > toUtc) continue;
                    sum += acc.Sum;
                    count += acc.Count;
                }
            }
            if (count > 0)
            {
                totals.Add(new MetricTotal(slug, sum, count));
            }
        }
        return totals.OrderByDescending(t => t.Count).ToList();
    }

    /// <summary>
    /// Serializa los buckets a JSONL de forma atómica (temp + <c>File.Move</c>),
    /// aplicando retención. Lo invoca el hosted service. No corre en el hot-path.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var cutoff = RetentionCutoffUtc();
        var lines = new List<string>();
        // Snapshot consistente por slug (lock por-slug), serialización fuera de lock.
        foreach (var (slug, slugBuckets) in _buckets)
        {
            List<KeyValuePair<DateTime, Accumulator>> copy;
            lock (slugBuckets.Gate)
            {
                copy = slugBuckets.ByHour
                    .Where(kv => kv.Key >= cutoff)
                    .Select(kv => new KeyValuePair<DateTime, Accumulator>(kv.Key, kv.Value.Copy()))
                    .ToList();
            }
            foreach (var (bucketUtc, acc) in copy)
            {
                lines.Add(JsonSerializer.Serialize(
                    new ProjectionLine(slug, bucketUtc, acc.Sum, acc.Count)));
            }
        }

        try
        {
            var path = ResolvePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + ".tmp";
            await File.WriteAllLinesAsync(tempPath, lines, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            lock (_flushLock)
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "InMemoryMetricsProjectionStore: flush a disco falló");
        }
    }

    private void Rehydrate()
    {
        var path = ResolvePath();
        if (!File.Exists(path)) return;
        var cutoff = RetentionCutoffUtc();
        try
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ProjectionLine? row;
                try { row = JsonSerializer.Deserialize<ProjectionLine>(line); }
                catch (JsonException) { continue; }
                if (row is null || string.IsNullOrWhiteSpace(row.Slug)) continue;

                var bucketUtc = DateTime.SpecifyKind(row.BucketUtc, DateTimeKind.Utc);
                if (bucketUtc < cutoff) continue;

                var slugBuckets = _buckets.GetOrAdd(row.Slug, static _ => new SlugBuckets());
                lock (slugBuckets.Gate)
                {
                    if (!slugBuckets.ByHour.TryGetValue(bucketUtc, out var acc))
                    {
                        acc = new Accumulator();
                        slugBuckets.ByHour[bucketUtc] = acc;
                    }
                    acc.Sum += row.Sum;
                    acc.Count += row.Count;
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "InMemoryMetricsProjectionStore: rehidratación falló");
        }
    }

    private DateTime RetentionCutoffUtc()
    {
        var days = _settings.Value.ProjectionRetentionDays;
        return days <= 0 ? DateTime.MinValue : DateTime.UtcNow.Date.AddDays(-days);
    }

    private static DateTime FloorToHour(DateTime value)
    {
        var u = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(u.Year, u.Month, u.Day, u.Hour, 0, 0, DateTimeKind.Utc);
    }

    private string ResolvePath() => Path.Combine(
        _hostEnvironment.ContentRootPath, "App_Data", DirectorySegment1, DirectorySegment2, FileName);

    private sealed class SlugBuckets
    {
        public object Gate { get; } = new();
        public Dictionary<DateTime, Accumulator> ByHour { get; } = new();
    }

    private sealed class Accumulator
    {
        public decimal Sum;
        public int Count;
        public Accumulator Copy() => new() { Sum = Sum, Count = Count };
    }

    private sealed record ProjectionLine(string Slug, DateTime BucketUtc, decimal Sum, int Count);
}
