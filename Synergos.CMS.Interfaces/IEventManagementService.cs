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
/// <param name="EventId">
/// De qué evento era la entrada. Opcional (aditivo, T7): lo necesita el aviso EN VIVO
/// para elegir el canal — sin él no se sabe a qué consola avisar. Viene vacío cuando el
/// token no se pudo verificar (no hay evento del que hablar).
/// </param>
public sealed record EventCheckInResult(
    string Status,
    string? EventId = null,
    string? TicketId = null,
    string? AttendeeName = null);

/// <summary>
/// Un tier del borrador de evento del organizador: nombre visible + precio +
/// aforo. El <see cref="Capacity"/> debe ser &gt; 0.
/// </summary>
public sealed record EventTierDraft(string Name, decimal Price, int Capacity);

/// <summary>
/// Borrador de evento nuevo que arma el organizador (cara de creación). Se valida
/// y se publica en el catálogo vía <see cref="IEventManagementService.CreateEventAsync"/>.
/// <see cref="SeatMap"/> es opcional (modo reserved); sin él, el evento es modo
/// general (selección por cantidad de tier).
/// </summary>
public sealed record EventDraft(
    string Name,
    string Venue,
    DateTimeOffset Date,
    IReadOnlyList<EventTierDraft> Tiers,
    EventSeatMap? SeatMap = null,
    string? City = null,
    string? Category = null,
    string? Currency = null,
    string? Description = null,
    string? Organizer = null,
    string? ImageUrl = null);

/// <summary>Resultado de crear un evento: el id asignado al evento publicado.</summary>
public sealed record EventCreateResult(string EventId);

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

    /// <summary>
    /// Publica un evento nuevo del organizador en el catálogo (aparece en el
    /// search desde ese momento). Valida el borrador (nombre/venue obligatorios,
    /// al menos un tier, cada aforo &gt; 0) y delega la publicación al
    /// <see cref="IEventCatalogProvider.PublishEventAsync"/> (DIP — no conoce el
    /// almacenamiento). Devuelve el id del evento publicado. Lanza
    /// <see cref="ArgumentException"/> si el borrador es inválido.
    /// </summary>
    Task<EventCreateResult> CreateEventAsync(EventDraft draft, CancellationToken cancellationToken = default);
}
