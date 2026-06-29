using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del vertical <strong>Eventos</strong> (OLA 6 — plataforma de eventos
/// enterprise, doc eventos-app-spec). La consumen los módulos Angular
/// <c>eventos-ticketing</c> (cara de asistente) y <c>eventos-manager</c> (cara de
/// organizador). Entrar al dominio = caer directo en la app real: catálogo → ficha →
/// seleccionar (tier/asiento) → pagar → confirmar (e-ticket QR) + dashboard/check-in.
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en los
/// seams (Application, sin Umbraco — ADR 0002), reusando el MOTOR:
/// <list type="bullet">
/// <item><see cref="IEventCatalogProvider"/> — catálogo + ficha (tiers + seat-map).</item>
/// <item><see cref="IEventTicketingService"/> — checkout (reusa
///   <see cref="IReservationService.HoldItemAsync"/> + <see cref="IPaymentProvider"/>)
///   + confirm (captura + e-tickets QR). Idempotente por orderRef.</item>
/// <item><see cref="IEventManagementService"/> — manage (asistentes/aforo/vendidos)
///   + check-in (idempotente).</item>
/// </list>
/// El precio se formatea es-CO vía <see cref="IPriceFormatter"/>. Contrato (lo programa
/// el agente UI): <c>GET events?q · GET event/{id} · POST checkout · POST confirm ·
/// GET manage/{eventId} · POST checkin</c>.
/// </remarks>
[ApiController]
[Route("api/eventos")]
public sealed class EventosController : ControllerBase
{
    private readonly IEventCatalogProvider _catalog;
    private readonly IEventTicketingService _ticketing;
    private readonly IEventManagementService _management;
    private readonly IPriceFormatter _priceFormatter;

    public EventosController(
        IEventCatalogProvider catalog,
        IEventTicketingService ticketing,
        IEventManagementService management,
        IPriceFormatter priceFormatter)
    {
        _catalog = catalog;
        _ticketing = ticketing;
        _management = management;
        _priceFormatter = priceFormatter;
    }

    // ── 1. Catálogo / agenda ───────────────────────────────────────────
    // GET /api/eventos/events?q= → { events:[...] }
    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var events = await _catalog.SearchAsync(q, cancellationToken);
        return Ok(new EventsResponse(events.Select(ToSummaryDto).ToList()));
    }

    // ── 2. Ficha de evento ─────────────────────────────────────────────
    // GET /api/eventos/event/{id} → { event, tiers:[...], seatmap }
    [HttpGet("event/{id}")]
    public async Task<IActionResult> Event(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del evento es requerido." });
        }

        var detail = await _catalog.GetEventAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Evento '{id}' no encontrado." });
        }

        return Ok(new EventDetailResponse(
            Event: ToSummaryDto(detail.Summary),
            Description: detail.Description,
            Organizer: detail.Organizer,
            Tiers: detail.Tiers.Select(ToTierDto).ToList(),
            SeatMap: ToSeatMapDto(detail.SeatMap)));
    }

    // ── 3. Checkout (apartar + abrir sesión de pago) ────────────────────
    // POST /api/eventos/checkout { eventId, items:[{tier,seat?,qty}], attendees:[...] }
    //   → { orderRef, paymentSessionId, amount, currency }
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.EventId))
        {
            return BadRequest(new { error = "eventId es requerido." });
        }

        var items = (request.Items ?? Array.Empty<CheckoutItemRequest>())
            .Select(i => new EventCheckoutItem(i.Tier, i.Seat, i.Qty))
            .ToList();
        var attendees = (request.Attendees ?? Array.Empty<AttendeeRequest>())
            .Select(a => new EventAttendeeInfo(a.Name, a.Email, a.DocumentId))
            .ToList();

        EventCheckoutResult result;
        try
        {
            result = await _ticketing.CheckoutAsync(request.EventId.Trim(), items, attendees, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new CheckoutResponse(
            OrderRef: result.OrderRef,
            PaymentSessionId: result.PaymentSessionId,
            Amount: result.Amount,
            AmountFormatted: _priceFormatter.Format(result.Amount, result.Currency),
            Currency: result.Currency));
    }

    // ── 4. Confirm (capturar + emitir e-tickets QR) ─────────────────────
    // POST /api/eventos/confirm { orderRef } → { status, tickets:[{id, qr}] }
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderRef))
        {
            return BadRequest(new { error = "orderRef es requerido." });
        }

        EventConfirmationResult result;
        try
        {
            result = await _ticketing.ConfirmAsync(request.OrderRef.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ConfirmResponse(
            Status: result.Status,
            Tickets: result.Tickets.Select(ToTicketDto).ToList()));
    }

    // ── 5. Manage (dashboard de organizador) ────────────────────────────
    // GET /api/eventos/manage/{eventId} → { attendees:[...], capacity, sold }
    [HttpGet("manage/{eventId}")]
    public async Task<IActionResult> Manage(string eventId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return BadRequest(new { error = "eventId es requerido." });
        }

        EventManageView view;
        try
        {
            view = await _management.GetManageAsync(eventId.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        return Ok(new ManageResponse(
            Attendees: view.Attendees.Select(ToAttendeeDto).ToList(),
            Capacity: view.Capacity,
            Sold: view.Sold));
    }

    // ── 6. Check-in (validar + marcar asistencia, idempotente) ──────────
    // POST /api/eventos/checkin { ticketId } → { status }
    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TicketId))
        {
            return BadRequest(new { error = "ticketId es requerido." });
        }

        var result = await _management.CheckInAsync(request.TicketId.Trim(), cancellationToken);
        return Ok(new CheckInResponse(result.Status));
    }

    // ── Mappers a DTOs JSON estables ────────────────────────────────────

    private EventSummaryDto ToSummaryDto(EventSummary s) => new(
        Id: s.Id,
        Slug: s.Slug,
        Title: s.Title,
        Category: s.Category,
        City: s.City,
        Venue: s.Venue,
        StartUtc: s.StartUtc,
        ImageUrl: s.ImageUrl,
        PriceFrom: s.PriceFrom,
        PriceFromFormatted: _priceFormatter.Format(s.PriceFrom, s.Currency),
        Currency: s.Currency,
        Mode: s.Mode);

    private EventTierDto ToTierDto(EventTier t) => new(
        Code: t.Code,
        Name: t.Name,
        Price: t.Price,
        PriceFormatted: _priceFormatter.Format(t.Price, t.Currency),
        Currency: t.Currency,
        Capacity: t.Capacity,
        Remaining: t.Remaining,
        MaxPerOrder: t.MaxPerOrder,
        ZoneId: t.ZoneId);

    private EventSeatMapDto? ToSeatMapDto(EventSeatMap? map)
    {
        if (map is null)
        {
            return null;
        }
        return new EventSeatMapDto(
            VenueName: map.VenueName,
            Zones: map.Zones.Select(z => new EventZoneDto(
                Id: z.Id,
                Name: z.Name,
                Price: z.Price,
                PriceFormatted: _priceFormatter.Format(z.Price, z.Currency),
                Currency: z.Currency,
                TierCode: z.TierCode,
                Rows: z.Rows.Select(r => new EventRowDto(
                    Label: r.Label,
                    Seats: r.Seats.Select(seat => new EventSeatDto(seat.Id, seat.Label, seat.Status)).ToList()))
                    .ToList()))
                .ToList());
    }

    private static EventTicketDto ToTicketDto(EventTicket t) => new(
        Id: t.Id,
        Qr: t.Qr,
        EventId: t.EventId,
        AttendeeName: t.AttendeeName,
        Tier: t.Tier,
        Seat: t.Seat);

    private static EventAttendeeDto ToAttendeeDto(EventAttendee a) => new(
        TicketId: a.TicketId,
        Name: a.Name,
        Email: a.Email,
        Tier: a.Tier,
        Seat: a.Seat,
        CheckedIn: a.CheckedIn);

    // ── Request DTOs (binding del módulo Angular) ───────────────────────

    public sealed record CheckoutItemRequest(string Tier, string? Seat, int Qty);

    public sealed record AttendeeRequest(string Name, string Email, string? DocumentId);

    public sealed record CheckoutRequest(
        string EventId,
        IReadOnlyList<CheckoutItemRequest>? Items,
        IReadOnlyList<AttendeeRequest>? Attendees);

    public sealed record ConfirmRequest(string OrderRef);

    public sealed record CheckInRequest(string TicketId);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────────

    public sealed record EventSummaryDto(
        string Id,
        string Slug,
        string Title,
        string Category,
        string City,
        string Venue,
        DateTimeOffset StartUtc,
        string ImageUrl,
        decimal PriceFrom,
        string PriceFromFormatted,
        string Currency,
        string Mode);

    public sealed record EventsResponse(IReadOnlyList<EventSummaryDto> Events);

    public sealed record EventTierDto(
        string Code,
        string Name,
        decimal Price,
        string PriceFormatted,
        string Currency,
        int Capacity,
        int Remaining,
        int MaxPerOrder,
        string? ZoneId);

    public sealed record EventSeatDto(string Id, string Label, string Status);

    public sealed record EventRowDto(string Label, IReadOnlyList<EventSeatDto> Seats);

    public sealed record EventZoneDto(
        string Id,
        string Name,
        decimal Price,
        string PriceFormatted,
        string Currency,
        string TierCode,
        IReadOnlyList<EventRowDto> Rows);

    public sealed record EventSeatMapDto(string VenueName, IReadOnlyList<EventZoneDto> Zones);

    public sealed record EventDetailResponse(
        EventSummaryDto Event,
        string Description,
        string Organizer,
        IReadOnlyList<EventTierDto> Tiers,
        // Contrato exacto: la clave es "seatmap" (todo minúscula), null en eventos
        // modo general. El default camelCase daría "seatMap"; lo fijamos al contrato.
        [property: JsonPropertyName("seatmap")] EventSeatMapDto? SeatMap);

    public sealed record CheckoutResponse(
        string OrderRef,
        string PaymentSessionId,
        decimal Amount,
        string AmountFormatted,
        string Currency);

    public sealed record EventTicketDto(
        string Id,
        string Qr,
        string EventId,
        string AttendeeName,
        string Tier,
        string? Seat);

    public sealed record ConfirmResponse(string Status, IReadOnlyList<EventTicketDto> Tickets);

    public sealed record EventAttendeeDto(
        string TicketId,
        string Name,
        string Email,
        string Tier,
        string? Seat,
        bool CheckedIn);

    public sealed record ManageResponse(
        IReadOnlyList<EventAttendeeDto> Attendees,
        int Capacity,
        int Sold);

    public sealed record CheckInResponse(string Status);
}
