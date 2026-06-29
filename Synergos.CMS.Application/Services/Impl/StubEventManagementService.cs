using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IEventManagementService"/> — cara de organizador (operacional)
/// del vertical Eventos (doc eventos-app-spec §2 cara B). Lee los tickets confirmados
/// del motor de ticketing (<see cref="StubEventTicketingService"/>) y el aforo del
/// catálogo (<see cref="IEventCatalogProvider"/>) para armar el dashboard de
/// asistentes/aforo/vendidos, y delega el check-in (validación + marca idempotente)
/// al motor de ticketing — fuente de verdad de los tickets.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica el estado de los tickets: lo COMPONE
/// (DIP) del <see cref="StubEventTicketingService"/> concreto vía
/// <c>GetConfirmedTickets</c> / <c>MarkCheckedIn</c> (mismo patrón que
/// <c>StubContentStream</c> sobre <c>StubReactionService</c>). <see cref="CheckInAsync"/>
/// es IDEMPOTENTE — el primer check-in devuelve <c>valid</c>, los siguientes
/// <c>already-used</c>, núcleo anti-doble-entrada. El adapter real (DB / scanner)
/// implementa la misma seam. ADR 0075.
/// </remarks>
public sealed class StubEventManagementService : IEventManagementService
{
    private readonly StubEventTicketingService _ticketing;
    private readonly IEventCatalogProvider _catalog;

    public StubEventManagementService(StubEventTicketingService ticketing, IEventCatalogProvider catalog)
    {
        _ticketing = ticketing ?? throw new ArgumentNullException(nameof(ticketing));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<EventManageView> GetManageAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("El evento es obligatorio.", nameof(eventId));
        }

        var detail = await _catalog.GetEventAsync(eventId, cancellationToken)
            ?? throw new ArgumentException($"Evento '{eventId}' no encontrado.", nameof(eventId));

        var attendees = _ticketing.GetConfirmedTickets(detail.Summary.Id);

        // Aforo total = suma de la capacidad de los tiers. Vendidos = tickets
        // confirmados (las unidades emitidas por el motor de ticketing).
        var capacity = detail.Tiers.Sum(t => t.Capacity);
        var sold = attendees.Count;

        return new EventManageView(
            EventId: detail.Summary.Id,
            Attendees: attendees,
            Capacity: capacity,
            Sold: sold);
    }

    public Task<EventCheckInResult> CheckInAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        var status = _ticketing.MarkCheckedIn(ticketId);
        return Task.FromResult(new EventCheckInResult(status));
    }
}
