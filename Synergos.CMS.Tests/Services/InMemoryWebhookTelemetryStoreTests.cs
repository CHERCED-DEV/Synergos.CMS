using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="InMemoryWebhookTelemetryStore"/> (Olas 165-166,
/// ADR 0072). Verifica ring buffer behavior, percentile computation,
/// channel isolation, success/failure counters.
/// </summary>
public class InMemoryWebhookTelemetryStoreTests
{
    [Fact]
    public void GetChannelStats_EmptyStore_ReturnsEmptyList()
    {
        var store = new InMemoryWebhookTelemetryStore();

        var stats = store.GetChannelStats();

        Assert.Empty(stats);
    }

    [Fact]
    public void RecordOutcome_SingleCall_PopulatesStats()
    {
        var store = new InMemoryWebhookTelemetryStore();

        store.RecordOutcome("comment-moderation-webhook", TimeSpan.FromMilliseconds(150), 200, true);

        var stats = store.GetChannelStats();
        Assert.Single(stats);
        var stat = stats[0];
        Assert.Equal("comment-moderation-webhook", stat.ChannelName);
        Assert.Equal(1, stat.TotalCalls);
        Assert.Equal(1, stat.SuccessCount);
        Assert.Equal(0, stat.FailureCount);
        Assert.Equal(150, stat.P50LatencyMs);
        Assert.NotNull(stat.LastObservedUtc);
    }

    [Fact]
    public void RecordOutcome_MultipleChannels_IsolatedStats()
    {
        var store = new InMemoryWebhookTelemetryStore();

        store.RecordOutcome("channel-a", TimeSpan.FromMilliseconds(100), 200, true);
        store.RecordOutcome("channel-b", TimeSpan.FromMilliseconds(500), 500, false);

        var stats = store.GetChannelStats();
        Assert.Equal(2, stats.Count);
        var a = stats.First(s => s.ChannelName == "channel-a");
        var b = stats.First(s => s.ChannelName == "channel-b");
        Assert.Equal(1, a.SuccessCount);
        Assert.Equal(0, a.FailureCount);
        Assert.Equal(0, b.SuccessCount);
        Assert.Equal(1, b.FailureCount);
    }

    [Fact]
    public void GetChannelStats_HundredSamples_ComputesPercentiles()
    {
        var store = new InMemoryWebhookTelemetryStore();
        // 100 samples: 1ms, 2ms, ..., 100ms.
        for (var i = 1; i <= 100; i++)
        {
            store.RecordOutcome("test", TimeSpan.FromMilliseconds(i), 200, true);
        }

        var stat = store.GetChannelStats().Single();
        Assert.Equal(100, stat.TotalCalls);
        // P50 of 1-100 sorted: index 49 (Math.Ceiling(0.50 * 100) - 1 = 49) → 50ms.
        Assert.Equal(50, stat.P50LatencyMs);
        // P95: index 94 → 95ms.
        Assert.Equal(95, stat.P95LatencyMs);
        // P99: index 98 → 99ms.
        Assert.Equal(99, stat.P99LatencyMs);
    }

    [Fact]
    public void RecordOutcome_RingBuffer_OverflowsKeepingLatest()
    {
        var store = new InMemoryWebhookTelemetryStore();
        // Record 1500 samples — buffer max is 1000.
        for (var i = 0; i < 1500; i++)
        {
            store.RecordOutcome("test", TimeSpan.FromMilliseconds(i), 200, true);
        }

        var stat = store.GetChannelStats().Single();
        Assert.Equal(1500, stat.TotalCalls);
        // Buffer holds last 1000 = samples 500..1499. P50 → ~999ms.
        Assert.True(stat.P50LatencyMs >= 999 && stat.P50LatencyMs <= 1000,
            $"P50 expected ~999-1000ms but got {stat.P50LatencyMs}");
    }

    [Fact]
    public void GetChannelStats_OrderedByChannelName()
    {
        var store = new InMemoryWebhookTelemetryStore();
        store.RecordOutcome("zeta", TimeSpan.FromMilliseconds(1), 200, true);
        store.RecordOutcome("alpha", TimeSpan.FromMilliseconds(1), 200, true);
        store.RecordOutcome("mu", TimeSpan.FromMilliseconds(1), 200, true);

        var stats = store.GetChannelStats();
        Assert.Equal("alpha", stats[0].ChannelName);
        Assert.Equal("mu", stats[1].ChannelName);
        Assert.Equal("zeta", stats[2].ChannelName);
    }
}
