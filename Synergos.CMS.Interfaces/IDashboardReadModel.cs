namespace Synergos.CMS.Interfaces;

/// <summary>
/// Fachada de lectura del Dashboard (ADR 0097). Compone las fuentes vivas
/// (proyección de métricas + checkouts + búsqueda + roster + webhooks) en
/// ViewModels listos para graficar.
/// </summary>
/// <remarks>
/// <para>La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultDashboardReadModel</c>. Es solo-lectura
/// y sin efectos secundarios — deliberadamente NO usa
/// <c>ICartAbandonmentTracker.DetectAbandoned()</c> (que muta estado); el
/// abandono se lee de la proyección del slug <c>cart.abandoned</c>.</para>
///
/// <para><b>Consumidor: uno.</b> <c>DashboardApiController</c>, que sirve la app Angular
/// <c>&lt;synergos-dashboard&gt;</c>. Aquí decía que la consumía "tanto el <c>/admin</c> SSR
/// como" esa app; se verificó y <b>es falso</b>: el dashboard SSR no la inyecta. Era el plan,
/// no el estado. Se corrige porque una interfaz que declara dos consumidores para justificarse
/// y tiene uno es justo lo que hace que nadie confíe en los comentarios.</para>
///
/// <para><b>Por qué sigue existiendo con un solo consumidor</b> (auditoría, backlog Bajo #10).
/// El proyecto no crea interfaces con una sola implementación salvo que sean seam genuina. Ésta
/// se gana el sitio por lo que ahorra en el borde: colapsa CINCO seams en una dependencia, así
/// que el test del controller sustituye un doble en vez de montar cinco. Colapsarla contra su
/// impl no quitaría una abstracción — movería esas cinco dependencias al controller y a su
/// test. Queda como está; si algún día el <c>/admin</c> SSR la consume de verdad, el motivo
/// original vuelve a ser cierto.</para>
/// </remarks>
public interface IDashboardReadModel
{
    /// <summary>Ventas: ingresos, pedidos, ticket promedio, carritos y abandono.</summary>
    SalesOverviewVm GetSalesOverview(DateTime fromUtc, DateTime toUtc, MetricGranularity granularity);

    /// <summary>Progreso de usuarios: registros, logins y total de miembros.</summary>
    MemberProgressVm GetMemberProgress(DateTime fromUtc, DateTime toUtc, MetricGranularity granularity);

    /// <summary>Comportamiento: top de eventos por conteo en la ventana.</summary>
    BehaviorVm GetBehavior(DateTime fromUtc, DateTime toUtc, int topN);

    /// <summary>
    /// Búsqueda: top queries y top queries sin resultados.
    /// </summary>
    /// <remarks>
    /// Es la única cara async de esta fachada, y no por gusto: detrás puede haber un servicio
    /// de sesión fuera del proceso (ADR 0130). Las otras cinco leen de almacenes locales.
    /// </remarks>
    Task<SearchInsightsVm> GetSearchInsightsAsync(
        DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken = default);

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
