using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="DefaultDashboardReadModel"/> (ADR 0097 D2). Verifica
/// la composición de las 5 fuentes y los cálculos (revenue/AOV/series, conteos
/// desde la proyección, roster, búsqueda, webhooks, agente oculto).
/// </summary>
public sealed class DefaultDashboardReadModelTests
{
    private readonly IMetricsProjectionStore _metrics = Substitute.For<IMetricsProjectionStore>();
    private readonly ICheckoutRecorder _checkouts = Substitute.For<ICheckoutRecorder>();
    private readonly ISearchAnalyticsStore _search = Substitute.For<ISearchAnalyticsStore>();
    private readonly IMemberRosterReader _roster = Substitute.For<IMemberRosterReader>();
    private readonly IWebhookTelemetryStore _webhooks = Substitute.For<IWebhookTelemetryStore>();

    private DefaultDashboardReadModel BuildSut() =>
        new(_metrics, _checkouts, _search, _roster, _webhooks);

    private static CheckoutCompleted Order(decimal subtotal, DateTime when) =>
        new(Guid.NewGuid().ToString("N"), Array.Empty<CheckoutLineItem>(), subtotal, "COP", when);

    [Fact]
    public void GetSalesOverview_AggregatesRevenueAovAndSeries()
    {
        var d1 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var d2 = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        _checkouts.GetCheckouts(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            Order(100m, d1), Order(200m, d1.AddHours(2)), Order(50m, d2),
        });
        _metrics.GetTotals(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            new MetricTotal("cart.item-added", 0m, 5),
            new MetricTotal("cart.abandoned", 0m, 2),
        });

        var vm = BuildSut().GetSalesOverview(d1.AddDays(-1), d2.AddDays(1), MetricGranularity.Day);

        Assert.Equal(350m, vm.TotalRevenue);
        Assert.Equal(3, vm.OrderCount);
        Assert.Equal(116.67m, vm.AverageOrderValue);
        Assert.Equal("COP", vm.Currency);
        Assert.Equal(5, vm.CartAddCount);
        Assert.Equal(2, vm.AbandonedCount);
        Assert.Equal(2, vm.RevenueSeries.Count);                 // 2 días
        Assert.Equal(300m, vm.RevenueSeries[0].Sum);             // día 1: 100+200
        Assert.Equal(50m, vm.RevenueSeries[1].Sum);              // día 2
    }

    [Fact]
    public void GetSalesOverview_NoOrders_ZeroesNotDivideByZero()
    {
        _checkouts.GetCheckouts(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<CheckoutCompleted>());
        _metrics.GetTotals(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<MetricTotal>());

        var vm = BuildSut().GetSalesOverview(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, MetricGranularity.Day);

        Assert.Equal(0m, vm.TotalRevenue);
        Assert.Equal(0, vm.OrderCount);
        Assert.Equal(0m, vm.AverageOrderValue);
        Assert.Empty(vm.RevenueSeries);
        Assert.Equal(0, vm.CartAddCount);
    }

    [Fact]
    public void GetMemberProgress_ComposesRosterAndSeries()
    {
        _roster.GetRosterPage(1, 1).Returns(new MemberRosterPage(Array.Empty<MemberRosterItem>(), 1, 1, 42, null));
        _roster.ListAllRoles().Returns(new[] { "admin", "premium" });
        _metrics.GetSeries("account.registered", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>())
            .Returns(new MetricSeries("account.registered", MetricGranularity.Day, Array.Empty<MetricDataPoint>()));
        _metrics.GetSeries("account.login", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>())
            .Returns(new MetricSeries("account.login", MetricGranularity.Day, Array.Empty<MetricDataPoint>()));

        var vm = BuildSut().GetMemberProgress(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, MetricGranularity.Day);

        Assert.Equal(42, vm.TotalMembers);
        Assert.Equal(new[] { "admin", "premium" }, vm.Roles);
        Assert.Equal("account.registered", vm.Registrations.Slug);
        Assert.Equal("account.login", vm.Logins.Slug);
    }

    [Fact]
    public void GetBehavior_AppliesTopN()
    {
        var totals = Enumerable.Range(0, 25)
            .Select(i => new MetricTotal($"e{i}", 0m, 25 - i)).ToList();
        _metrics.GetTotals(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(totals);

        var vm = BuildSut().GetBehavior(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, topN: 20);

        Assert.Equal(20, vm.TopEvents.Count);
        Assert.Equal("e0", vm.TopEvents[0].Slug);
    }

    [Fact]
    public void GetSearchInsights_PassesThroughStore()
    {
        _search.GetTopQueries(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(new[] { new SearchQueryStat("camisa", 9, 4, DateTime.UtcNow) });
        _search.GetTopNoResultQueries(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(new[] { new SearchQueryStat("xyz", 3, 0, DateTime.UtcNow) });

        var vm = BuildSut().GetSearchInsights(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 10);

        Assert.Single(vm.TopQueries);
        Assert.Equal("camisa", vm.TopQueries[0].Query);
        Assert.Equal("xyz", vm.NoResultQueries[0].Query);
    }

    [Fact]
    public void GetWebhookHealth_ReturnsChannelStats()
    {
        _webhooks.GetChannelStats().Returns(new[]
        {
            new ChannelTelemetrySnapshot("teams", 10, 9, 1, 12.0, 30.0, 45.0, DateTime.UtcNow),
        });

        var vm = BuildSut().GetWebhookHealth();

        Assert.Single(vm.Channels);
        Assert.Equal("teams", vm.Channels[0].ChannelName);
    }

    [Fact]
    public void GetAgentInsights_NotAvailableUntilAiLayerBuilt()
    {
        var vm = BuildSut().GetAgentInsights();
        Assert.False(vm.Available);
    }
}
