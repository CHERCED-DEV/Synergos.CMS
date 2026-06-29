namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resultado de capturar un lead (contacto con el agente): el id asignado al
/// lead generado. El lead es el caso degenerado del "confirmar" transaccional
/// (captura de intención sin slot ni pago — spec §4).
/// </summary>
public sealed record LeadResult(string LeadId);

/// <summary>
/// Servicio de captura de leads del vertical Propiedades. Es el otro CTA central
/// de la PDP (contactar agente, doc propiedades-app-spec §2): genera un lead a
/// partir del listado de interés + datos de contacto + mensaje, ruteable al
/// agente.
/// </summary>
/// <remarks>
/// Seam stub-first: el default <c>StubLeadCaptureService</c> (Application, lógica
/// pura) persiste el lead en memoria del proceso; el adapter real lo rutea a un
/// CRM/email del agente sin tocar el motor. REUSA (no crea seams nuevos):
/// <see cref="IAuditTrailWriter"/> (ADR 0037) deja rastro append-only forense del
/// lead, e <see cref="IAnalyticsTracker"/> (ADR 0067) emite el evento de negocio
/// <c>realty.lead-captured</c> (un KPI del marketplace). Ambos son opcionales en
/// el ctor (null = no-op) para que los tests del seam corran aislados. ADR 0002 +
/// ADR 0075.
/// </remarks>
public interface ILeadCaptureService
{
    /// <summary>
    /// Captura un lead de contacto con el agente del listado y devuelve su id.
    /// Lanza <see cref="ArgumentException"/> si el listado, el contacto o el
    /// mensaje son inválidos.
    /// </summary>
    Task<LeadResult> CaptureAsync(string listingId, VisitContact contact, string message, CancellationToken cancellationToken = default);
}
