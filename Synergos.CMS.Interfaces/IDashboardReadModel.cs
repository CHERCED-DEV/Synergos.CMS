namespace Synergos.CMS.Interfaces;

/// <summary>
/// Fachada de lectura del Dashboard (ADR 0097). Compone las fuentes vivas
/// (proyección de métricas + checkouts + búsqueda + roster + webhooks) en
/// ViewModels listos para graficar. Una sola verdad: la consume tanto el
/// <c>/admin</c> SSR como la app Angular <c>&lt;synergos-dashboard&gt;</c> vía
/// <c>DashboardApiController</c>.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultDashboardReadModel</c>. Es solo-lectura
/// y sin efectos secundarios — deliberadamente NO usa
/// <c>ICartAbandonmentTracker.DetectAbandoned()</c> (que muta estado); el
/// abandono se lee de la proyección del slug <c>cart.abandoned</c>.
/// </remarks>
public interface IDashboardReadModel
{
    /// <summary>Ventas: ingresos, pedidos, ticket promedio, carritos y abandono.</summary>
    SalesOverviewVm GetSalesOverview(DateTime fromUtc, DateTime toUtc, MetricGranularity granularity);

    /// <summary>Progreso de usuarios: registros, logins y total de miembros.</summary>
    MemberProgressVm GetMemberProgress(DateTime fromUtc, DateTime toUtc, MetricGranularity granularity);

    /// <summary>Comportamiento: top de eventos por conteo en la ventana.</summary>
    BehaviorVm GetBehavior(DateTime fromUtc, DateTime toUtc, int topN);

    /// <summary>Búsqueda: top queries y top queries sin resultados.</summary>
    SearchInsightsVm GetSearchInsights(DateTime fromUtc, DateTime toUtc, int limit);

    /// <summary>Salud de webhooks: stats por canal (latencias, éxito/fallo).</summary>
    WebhookHealthVm GetWebhookHealth();

    /// <summary>Insights del agente IA (ADR 0095) — oculto hasta que exista.</summary>
    AgentInsightsVm GetAgentInsights();
}

/// <summary>Resumen de ventas para el panel de ventas.</summary>
public sealed record SalesOverviewVm(
    decimal TotalRevenue,
    int OrderCount,
    decimal AverageOrderValue,
    string Currency,
    long CartAddCount,
    long AbandonedCount,
    IReadOnlyList<MetricDataPoint> RevenueSeries,
    MetricGranularity Granularity);

/// <summary>Progreso de miembros para el panel de usuarios.</summary>
public sealed record MemberProgressVm(
    long TotalMembers,
    IReadOnlyList<string> Roles,
    MetricSeries Registrations,
    MetricSeries Logins);

/// <summary>Top de eventos para el panel de comportamiento.</summary>
public sealed record BehaviorVm(IReadOnlyList<MetricTotal> TopEvents);

/// <summary>Insumos de búsqueda para el equipo editorial.</summary>
public sealed record SearchInsightsVm(
    IReadOnlyList<SearchQueryStat> TopQueries,
    IReadOnlyList<SearchQueryStat> NoResultQueries);

/// <summary>Salud de los canales de webhook.</summary>
public sealed record WebhookHealthVm(IReadOnlyList<ChannelTelemetrySnapshot> Channels);

/// <summary>
/// Insights del agente IA. <see cref="Available"/> es false hasta que la capa
/// IA (ADR 0095) esté construida; la UI oculta el panel mientras tanto.
/// </summary>
public sealed record AgentInsightsVm(bool Available, string Note);
