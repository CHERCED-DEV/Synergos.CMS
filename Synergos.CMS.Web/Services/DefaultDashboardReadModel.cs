using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IDashboardReadModel"/> (ADR 0097). Compone la
/// proyección de métricas + checkouts + búsqueda + roster + webhooks en
/// ViewModels. Solo-lectura, sin efectos secundarios.
/// </summary>
/// <remarks>
/// Lifetime Scoped (no Singleton): <see cref="IMemberRosterReader"/> depende
/// de servicios per-request de Umbraco; capturarlo en un singleton sería una
/// captive dependency.
/// </remarks>
public sealed class DefaultDashboardReadModel : IDashboardReadModel
{
    // Slugs que emiten los consumidores (deben coincidir con los Track()
    // de Shop/Account/CartAbandonmentScanner — ADR 0037).
    private const string SlugCartAdded = "cart.item-added";
    private const string SlugCartAbandoned = "cart.abandoned";
    private const string SlugRegister = "account.registered";   // AccountController emite "account.registered"
    private const string SlugLogin = "account.login";

    private readonly IMetricsProjectionStore _metrics;
    private readonly ICheckoutRecorder _checkouts;
    private readonly ISearchAnalyticsStore _search;
    private readonly IMemberRosterReader _roster;
    private readonly IWebhookTelemetryStore _webhooks;

    public DefaultDashboardReadModel(
        IMetricsProjectionStore metrics,
        ICheckoutRecorder checkouts,
        ISearchAnalyticsStore search,
        IMemberRosterReader roster,
        IWebhookTelemetryStore webhooks)
    {
        _metrics = metrics;
        _checkouts = checkouts;
        _search = search;
        _roster = roster;
        _webhooks = webhooks;
    }

    public SalesOverviewVm GetSalesOverview(DateTime fromUtc, DateTime toUtc, MetricGranularity granularity)
    {
        var orders = _checkouts.GetCheckouts(fromUtc, toUtc);
        var orderCount = orders.Count;
        var totalRevenue = orders.Sum(o => o.Subtotal);
        var aov = orderCount > 0 ? decimal.Round(totalRevenue / orderCount, 2) : 0m;
        var currency = orders.Count > 0 ? orders[0].Currency : "COP";

        var revenueSeries = orders
            .GroupBy(o => Bucket(o.OccurredUtc, granularity))
            .Select(g => new MetricDataPoint(g.Key, g.Sum(x => x.Subtotal), g.Count()))
            .OrderBy(p => p.BucketUtc)
            .ToList();

        var totals = _metrics.GetTotals(fromUtc, toUtc);
        var cartAdd = CountFor(totals, SlugCartAdded);
        var abandoned = CountFor(totals, SlugCartAbandoned);

        return new SalesOverviewVm(
            totalRevenue, orderCount, aov, currency, cartAdd, abandoned, revenueSeries, granularity);
    }

    public MemberProgressVm GetMemberProgress(DateTime fromUtc, DateTime toUtc, MetricGranularity granularity)
    {
        var total = _roster.GetRosterPage(1, 1).TotalCount;
        var roles = _roster.ListAllRoles();
        var registrations = _metrics.GetSeries(SlugRegister, fromUtc, toUtc, granularity);
        var logins = _metrics.GetSeries(SlugLogin, fromUtc, toUtc, granularity);
        return new MemberProgressVm(total, roles, registrations, logins);
    }

    public BehaviorVm GetBehavior(DateTime fromUtc, DateTime toUtc, int topN)
    {
        var top = _metrics.GetTotals(fromUtc, toUtc);
        if (topN > 0 && top.Count > topN)
        {
            top = top.Take(topN).ToList();
        }
        return new BehaviorVm(top);
    }

    public async Task<SearchInsightsVm> GetSearchInsightsAsync(
        DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken = default)
    {
        var safeLimit = limit <= 0 ? 10 : limit;
        return new SearchInsightsVm(
            await _search.GetTopQueriesAsync(fromUtc, toUtc, safeLimit, cancellationToken),
            await _search.GetTopNoResultQueriesAsync(fromUtc, toUtc, safeLimit, cancellationToken));
    }

    public WebhookHealthVm GetWebhookHealth() => new(_webhooks.GetChannelStats());

    public AgentInsightsVm GetAgentInsights() =>
        new(Available: false, Note: "La capa de IA (ADR 0095) aún no está construida.");

    private static long CountFor(IReadOnlyList<MetricTotal> totals, string slug) =>
        totals.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.Ordinal))?.Count ?? 0;

    private static DateTime Bucket(DateTime occurredUtc, MetricGranularity granularity)
    {
        var u = DateTime.SpecifyKind(occurredUtc, DateTimeKind.Utc);
        return granularity == MetricGranularity.Day
            ? new DateTime(u.Year, u.Month, u.Day, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(u.Year, u.Month, u.Day, u.Hour, 0, 0, DateTimeKind.Utc);
    }
}
