using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Decorator de <see cref="IAnalyticsTracker"/> (ADR 0097). Delega primero
/// en el tracker interno (logging) y luego proyecta el evento como un conteo
/// en el <see cref="IMetricsProjectionStore"/> para alimentar el dashboard.
/// </summary>
/// <remarks>
/// Reglas no-negociables (corre en el hot-path de ShopController/SearchController):
/// (a) O(1) en memoria, (b) try-catch-swallow — jamás lanza, (c) CERO IO
/// síncrono, (d) CERO await. Todo flush a disco vive en
/// <c>DashboardSnapshotFlushHostedService</c>. Cada evento se proyecta con
/// <c>Value=1m</c> — es un CONTADOR de eventos, no una magnitud; para
/// revenue/ponderado, usar <c>ICheckoutRecorder</c>.
///
/// Composición manual en <c>SeamComposer</c> — Umbraco 13 no trae Scrutor,
/// así que no hay <c>.Decorate()</c> automático.
/// </remarks>
public sealed class ProjectingAnalyticsTracker : IAnalyticsTracker
{
    private readonly IAnalyticsTracker _inner;
    private readonly IMetricsProjectionStore _projectionStore;
    private readonly ILogger<ProjectingAnalyticsTracker> _logger;

    public ProjectingAnalyticsTracker(
        IAnalyticsTracker inner,
        IMetricsProjectionStore projectionStore,
        ILogger<ProjectingAnalyticsTracker> logger)
    {
        _inner = inner;
        _projectionStore = projectionStore;
        _logger = logger;
    }

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        // 1) Comportamiento original primero (logging) — nunca se pierde.
        _inner.Track(eventName, properties);

        // 2) Proyección secundaria — best-effort, jamás rompe al caller.
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }
        try
        {
            _projectionStore.RecordEvent(new MetricEvent(eventName, DateTime.UtcNow, 1m));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProjectingAnalyticsTracker: proyección falló para {EventName}", eventName);
        }
    }
}
