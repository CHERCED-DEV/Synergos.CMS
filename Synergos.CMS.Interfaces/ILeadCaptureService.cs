namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resultado de capturar un lead (contacto con el agente): el id asignado al
/// lead generado. El lead es el caso degenerado del "confirmar" transaccional
/// (captura de intención sin slot ni pago — spec §4).
/// </summary>
public sealed record LeadResult(string LeadId);

/// <summary>
/// Estado del lead en el mini-CRM del agente (doc propiedades-app-spec §6 — consola
/// del agente, tablero kanban SH-5). El lead avanza <c>Nuevo → Contactado → Visita →
/// Cerrado</c> a medida que el agente lo trabaja.
/// </summary>
public enum LeadStatus
{
    /// <summary>Recién capturado — sin trabajar todavía (estado inicial).</summary>
    Nuevo,
    /// <summary>El agente ya contactó al interesado.</summary>
    Contactado,
    /// <summary>Se agendó/realizó una visita con el interesado.</summary>
    Visita,
    /// <summary>Lead cerrado (ganado o perdido) — estado terminal.</summary>
    Cerrado,
}

/// <summary>
/// Un lead visto desde la consola del agente (tarjeta del kanban): quién es el
/// interesado, sobre qué inmueble, su mensaje, su estado actual en el embudo y
/// cuándo se capturó. Es la proyección de lectura de <see cref="ILeadCaptureService.GetForAgentAsync"/>.
/// </summary>
public sealed record AgentLead(
    string LeadId,
    string AgentId,
    string ListingId,
    string Name,
    string Email,
    string? Phone,
    string Message,
    LeadStatus Status,
    DateTimeOffset CreatedAt);

/// <summary>Resultado de avanzar un lead: su nuevo estado en el embudo.</summary>
public sealed record LeadAdvanceResult(string LeadId, LeadStatus Status);

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

    /// <summary>
    /// Devuelve los leads asignados a un agente (mini-CRM, doc §6), del más reciente al
    /// más antiguo, para pintar el tablero kanban por estado. Lista vacía (no lanza) si
    /// el agente no tiene leads. El agente de cada lead se resuelve del inmueble de
    /// interés al capturarlo.
    /// </summary>
    Task<IReadOnlyList<AgentLead>> GetForAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Avanza el lead al estado <paramref name="status"/> del embudo y devuelve su nuevo
    /// estado. Idempotente (avanzar al estado actual no re-audita). Lanza
    /// <see cref="ArgumentException"/> si el lead no existe. Cada avance deja rastro en
    /// <see cref="IAuditTrailWriter"/> (evento <c>realty.lead-advanced</c>).
    /// </summary>
    Task<LeadAdvanceResult> AdvanceLeadAsync(string leadId, LeadStatus status, CancellationToken cancellationToken = default);
}
