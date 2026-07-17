namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de tracking de eventos de comportamiento del visitante. Aísla
/// a controllers/services del backend de telemetría (logs estructurados,
/// Mixpanel, Segment, custom data warehouse).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.LoggerAnalyticsTracker</c> y emite los
/// eventos como log estructurado con <c>ILogger</c>. Los logs son
/// agregables vía cualquier sink estándar (Serilog, Application Insights,
/// Elastic, etc.) que el operador del sitio configure.
///
/// Para producción con dashboard de eventos: swap a
/// <c>MixpanelAnalyticsTracker</c> / <c>SegmentAnalyticsTracker</c>
/// que invoquen el SDK del provider sin tocar consumidores.
///
/// Sin async — el tracking es fire-and-forget; el caller no debe
/// esperarlo. Si el provider necesita async (HTTP send), wrap en
/// <c>Task.Run</c> o queue interno del adapter.
/// </remarks>
public interface IAnalyticsTracker
{
    /// <summary>
    /// Registra un evento. <paramref name="eventName"/> debe ser slug
    /// estable (ej. "search.executed", "form.submitted",
    /// "cart.item-added"). <paramref name="properties"/> opcional, KV
    /// con metadata del evento.
    /// </summary>
    void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
}
