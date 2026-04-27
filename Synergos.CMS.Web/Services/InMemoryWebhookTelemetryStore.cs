using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IWebhookTelemetryStore"/> con ring buffer
/// in-memory por canal. Los samples se reset en restart. Cada canal
/// tiene un buffer independiente con last <see cref="MaxSamplesPerChannel"/>
/// outcomes.
/// </summary>
/// <remarks>
/// Thread-safe: <see cref="ConcurrentDictionary{TKey, TValue}"/> +
/// per-channel <c>lock</c> sobre el array circular. Reads computan
/// percentiles via copy + sort — O(N log N) pero N=1000 max.
///
/// Olas 165-166.
/// </remarks>
public sealed class InMemoryWebhookTelemetryStore : IWebhookTelemetryStore
{
    private const int MaxSamplesPerChannel = 1000;

    private sealed class ChannelBuffer
    {
        public readonly object Lock = new();
        public readonly long[] LatencyMs = new long[MaxSamplesPerChannel];
        public int Count;
        public int WriteIndex;
        public long TotalCalls;
        public long SuccessCount;
        public long FailureCount;
        public DateTime? LastObservedUtc;
    }

    private readonly ConcurrentDictionary<string, ChannelBuffer> _channels = new();

    public void RecordOutcome(
        string channelName,
        TimeSpan elapsed,
        int statusCode,
        bool isSuccess)
    {
        var buf = _channels.GetOrAdd(channelName, _ => new ChannelBuffer());
        var ms = (long)Math.Min(elapsed.TotalMilliseconds, int.MaxValue);
        lock (buf.Lock)
        {
            buf.LatencyMs[buf.WriteIndex] = ms;
            buf.WriteIndex = (buf.WriteIndex + 1) % MaxSamplesPerChannel;
            if (buf.Count < MaxSamplesPerChannel) buf.Count++;
            buf.TotalCalls++;
            if (isSuccess) buf.SuccessCount++;
            else buf.FailureCount++;
            buf.LastObservedUtc = DateTime.UtcNow;
        }
    }

    public IReadOnlyList<ChannelTelemetrySnapshot> GetChannelStats()
    {
        var results = new List<ChannelTelemetrySnapshot>();
        foreach (var (name, buf) in _channels)
        {
            long[] copy;
            int count;
            long total, success, failure;
            DateTime? lastObserved;
            lock (buf.Lock)
            {
                count = buf.Count;
                copy = new long[count];
                Array.Copy(buf.LatencyMs, copy, count);
                total = buf.TotalCalls;
                success = buf.SuccessCount;
                failure = buf.FailureCount;
                lastObserved = buf.LastObservedUtc;
            }

            if (count == 0)
            {
                results.Add(new ChannelTelemetrySnapshot(
                    name, total, success, failure, 0, 0, 0, lastObserved));
                continue;
            }

            Array.Sort(copy);
            results.Add(new ChannelTelemetrySnapshot(
                ChannelName: name,
                TotalCalls: total,
                SuccessCount: success,
                FailureCount: failure,
                P50LatencyMs: Percentile(copy, 0.50),
                P95LatencyMs: Percentile(copy, 0.95),
                P99LatencyMs: Percentile(copy, 0.99),
                LastObservedUtc: lastObserved));
        }

        return results
            .OrderBy(s => s.ChannelName, StringComparer.Ordinal)
            .ToList();
    }

    private static double Percentile(long[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0) return 0;
        var rank = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        rank = Math.Clamp(rank, 0, sortedSamples.Length - 1);
        return sortedSamples[rank];
    }
}
