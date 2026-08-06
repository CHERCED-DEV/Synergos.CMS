namespace Synergos.CMS.Interfaces;

/// <summary>
/// Timeline de estados de una orden/expediente — seam GENÉRICO (plan doc 21
/// §1.4 P4): la entidad "orden con timeline de etapas" es la misma en los 8
/// dominios (Order de Tienda ≈ Booking de viaje ≈ Ticket de evento ≈ Radicado
/// de trámite ≈ matrícula ≈ cita). El <c>tracking-timeline</c> de la UI la
/// renderiza igual en todas: etapas ordenadas + fecha en que se alcanzó cada
/// una + etapa actual. Un contrato, N dominios.
/// </summary>
/// <remarks>
/// Seam stub-first (ADR 0075): el default <c>StubOrderTrackingService</c>
/// (Application, lógica pura — ADR 0002) mantiene los timelines en memoria
/// del proceso sobre un pipeline de etapas configurable (default: el de
/// Tienda, pago → preparación → envío → entrega); un adapter real delega a
/// OMS/carrier/BPM sin tocar los consumidores. El seam es agnóstico del
/// dominio: NO valida que la orden exista — esa validación es del dominio
/// dueño (e.g. <see cref="IShopOrderService"/>, que alimenta este timeline al
/// confirmar el pago). <see cref="AdvanceAsync"/> es idempotente y monotónico:
/// avanzar a una etapa ya alcanzada (o anterior a la actual) no cambia nada.
/// </remarks>
public interface IOrderTrackingService
{
    /// <summary>
    /// Devuelve el timeline de la orden: todas las etapas del pipeline (en
    /// orden) con su fecha si ya se alcanzaron + la etapa actual. Devuelve
    /// <c>null</c> si la orden nunca registró una etapa (tracking no
    /// iniciado o ref desconocido).
    /// </summary>
    Task<OrderTimeline?> GetTimelineAsync(string orderRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Avanza la orden a la etapa <paramref name="stage"/> (slug del
    /// pipeline), sellando la fecha y la nota opcional; las etapas
    /// intermedias no alcanzadas se marcan alcanzadas (monotónico). Crea el
    /// timeline si es el primer avance de esa orden. Idempotente: avanzar a
    /// la etapa actual (o a una anterior) devuelve el timeline sin cambios.
    /// Lanza <see cref="ArgumentException"/> si el orderRef viene vacío o la
    /// etapa no pertenece al pipeline.
    /// </summary>
    Task<OrderTimeline> AdvanceAsync(
        string orderRef,
        string stage,
        string? note = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// El timeline de una orden: la etapa actual (slug) + todas las etapas del
/// pipeline en orden, cada una con su estado alcanzado/pendiente.
/// </summary>
public sealed record OrderTimeline(
    string OrderRef,
    string CurrentStage,
    IReadOnlyList<OrderTimelineStage> Stages,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Una etapa del timeline: slug estable (lo que la API mueve), label
/// human-facing es-CO (lo que la UI pinta), si ya se alcanzó, cuándo, y la
/// nota opcional sellada al alcanzarla (e.g. guía del carrier).
/// </summary>
public sealed record OrderTimelineStage(
    string Stage,
    string Label,
    bool Reached,
    DateTimeOffset? ReachedAt,
    string? Note);

/// <summary>
/// Definición de una etapa del pipeline (slug + label es-CO). Cada dominio
/// registra su pipeline al construir el tracker (Tienda: pago→preparación→
/// envío→entrega; Gobierno: radicado→en-revisión→resuelto; etc.).
/// </summary>
public sealed record OrderTrackingStageDefinition(string Stage, string Label);
