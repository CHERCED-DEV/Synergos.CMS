namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un asistente con ticket confirmado para un evento, con su estado de check-in.
/// Alimenta la tabla de asistentes de la cara de organizador.
/// <see cref="CheckedIn"/> = true tras un <see cref="IEventManagementService.CheckInAsync"/>
/// exitoso.
/// </summary>
public sealed record EventAttendee(
    string TicketId,
    string Name,
    string Email,
    string Tier,
    string? Seat,
    bool CheckedIn);

/// <summary>
/// Vista operacional de un evento (cara de organizador): asistentes + aforo +
/// vendidos. Es lo que el dashboard del organizador lista.
/// </summary>
public sealed record EventManageView(
    string EventId,
    IReadOnlyList<EventAttendee> Attendees,
    int Capacity,
    int Sold);

/// <summary>
/// Resultado de un check-in: <see cref="Status"/> = valid | already-used |
/// invalid. <c>valid</c> en el primer check-in exitoso; <c>already-used</c> si el
/// ticket ya estaba marcado; <c>invalid</c> si el ticket no existe.
/// </summary>
public sealed record EventCheckInResult(string Status);

/// <summary>
/// Cara de organizador (operacional) del vertical Eventos: dashboard de
/// asistentes/aforo + check-in onsite por ticket. No transacciona — opera sobre
/// los e-tickets ya emitidos por <see cref="IEventTicketingService"/>.
/// </summary>
/// <remarks>
/// Seam stub-first (ADR 0002): el default <c>StubEventManagementService</c>
/// (Application, lógica pura) lee el estado de las órdenes/tickets confirmados
/// del motor de ticketing (composición vía DIP, no duplica estado) y lleva las
/// marcas de check-in en memoria del proceso. <see cref="CheckInAsync"/> es
/// IDEMPOTENTE: el primer check-in válido devuelve <c>valid</c>, los siguientes
/// <c>already-used</c> — núcleo anti-doble-entrada de la operación onsite. El
/// adapter real (DB / scanner físico) implementa la misma seam. Tipos prefijados
/// <c>Event*</c> para no colisionar en el namespace Interfaces.
/// </remarks>
public interface IEventManagementService
{
    /// <summary>
    /// Devuelve la vista operacional del evento (asistentes confirmados + aforo
    /// total + vendidos). Si el evento no tiene ventas, <see cref="EventManageView.Attendees"/>
    /// es vacío y <see cref="EventManageView.Sold"/> es 0.
    /// </summary>
    Task<EventManageView> GetManageAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida y marca la asistencia de un ticket. Idempotente: <c>valid</c> en el
    /// primer check-in, <c>already-used</c> si ya estaba marcado, <c>invalid</c>
    /// si el ticket no existe.
    /// </summary>
    Task<EventCheckInResult> CheckInAsync(string ticketId, CancellationToken cancellationToken = default);
}
