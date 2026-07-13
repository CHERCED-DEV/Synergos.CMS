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
            Organizer: new EventOrganizerDto(detail.Organizer, string.Empty, string.Empty),
            Tiers: detail.Tiers.Select(ToTierDto).ToList(),
            SeatMap: ToSeatMapDto(detail.SeatMap),
            Venue: ToVenueDto(detail)));
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

    // ── 7. Mis tickets (cara de asistente) ──────────────────────────────
    // GET /api/eventos/tickets?holder= → { tickets:[...] }
    [HttpGet("tickets")]
    public async Task<IActionResult> Tickets([FromQuery] string? holder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(holder))
        {
            return BadRequest(new { error = "holder es requerido." });
        }

        var tickets = await _ticketing.GetTicketsAsync(holder.Trim(), cancellationToken);
        return Ok(new TicketsResponse(tickets.Select(ToTicketDto).ToList()));
    }

    // ── 8. Transferir ticket (reasigna holder + rota QR + auditado) ─────
    // POST /api/eventos/ticket/{id}/transfer { toEmail } → { ticket, newQr }
    [HttpPost("ticket/{id}/transfer")]
    public async Task<IActionResult> TransferTicket(
        string id,
        [FromBody] TransferRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del ticket es requerido." });
        }
        if (request is null || string.IsNullOrWhiteSpace(request.ToEmail))
        {
            return BadRequest(new { error = "toEmail es requerido." });
        }

        EventTicketTransferResult result;
        try
        {
            result = await _ticketing.TransferTicketAsync(id.Trim(), request.ToEmail.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new TransferResponse(ToTicketDto(result.Ticket), result.NewQr));
    }

    // ── 9. Crear evento (organizador → publica al catálogo) ─────────────
    // POST /api/eventos/event { draft } → { eventId }
    [HttpPost("event")]
    public async Task<IActionResult> CreateEvent([FromBody] EventDraftRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "name es requerido." });
        }

        var tiers = (request.Tiers ?? Array.Empty<EventTierDraftRequest>())
            .Select(t => new EventTierDraft(t.Name, t.Price, t.Capacity))
            .ToList();

        var draft = new EventDraft(
            Name: request.Name.Trim(),
            Venue: request.Venue ?? string.Empty,
            Date: request.Date,
            Tiers: tiers,
            SeatMap: null,
            City: request.City,
            Category: request.Category,
            Currency: request.Currency,
            Description: request.Description,
            Organizer: request.Organizer,
            ImageUrl: request.ImageUrl);

        EventCreateResult result;
        try
        {
            result = await _management.CreateEventAsync(draft, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new CreateEventResponse(result.EventId));
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
        StartsAt: s.StartUtc,   // la UI lee `startsAt` (mismo instante que startUtc)
        ImageUrl: s.ImageUrl,
        Cover: s.ImageUrl,
        PriceFrom: s.PriceFrom,
        FromAmount: s.PriceFrom,   // la UI lee `fromAmount` (número); sin esto todo salía "Gratis"
        PriceFromFormatted: _priceFormatter.Format(s.PriceFrom, s.Currency),
        Currency: s.Currency,
        Mode: s.Mode,
        Geo: s.Geo is null ? null : new EventGeoDto(s.Geo.Lat, s.Geo.Lng),
        Subtitle: string.IsNullOrWhiteSpace(s.Venue) ? s.City : $"{s.Venue} · {s.City}",
        Status: DeriveEventStatus(s.StartUtc),
        Badges: DeriveEventBadges(s.Mode));

    // Estado de ciclo de vida para la UI (EventStatus = upcoming|on-sale|sold-out|
    // past). El summary solo conoce la dimensión temporal (el aforo no vive en
    // EventSummary), así que derivamos por fecha: `past` si ya arrancó, si no
    // `upcoming`. on-sale/sold-out requieren aforo → no se emiten.
    private static string DeriveEventStatus(DateTimeOffset startUtc)
        => startUtc <= DateTimeOffset.UtcNow ? "past" : "upcoming";

    // Chips freeform del summary derivados del único campo con fuente (el modo de
    // venta): reserved → asientos numerados; general → entrada general.
    private static IReadOnlyList<string> DeriveEventBadges(string mode)
        => string.Equals(mode, "reserved", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Asientos numerados" }
            : new[] { "Entrada general" };

    private EventTierDto ToTierDto(EventTier t) => new(
        Id: t.Code,   // la UI lee `tier.id` para el checkout (mismo valor que code)
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

    // Proyecta el venue con la forma anidada que lee la ficha v2 (EventVenue →
    // VenueZone → SeatMapPayload): venue.zones[].seatmap.rows[].seats[]. Reusa el
    // seat-map del dominio; solo presente en eventos modo reserved (SeatMap != null).
    // La dirección de calle no existe en el dominio → cadena vacía; la ciudad viene
    // del summary. `type` del asiento = tier de la zona (price-level).
    private static EventVenueDto? ToVenueDto(EventDetail detail)
    {
        var map = detail.SeatMap;
        if (map is null)
        {
            return null;
        }
        return new EventVenueDto(
            Name: map.VenueName,
            Address: string.Empty,
            City: detail.Summary.City,
            Zones: map.Zones.Select(z => new EventVenueZoneDto(
                Id: z.Id,
                Name: z.Name,
                Amount: z.Price,
                Seatmap: new EventVenueSeatmapDto(
                    Rows: z.Rows.Select(r => new EventVenueRowDto(
                        RowNumber: r.Label,
                        Seats: r.Seats.Select(seat => new EventVenueSeatDto(
                            Id: seat.Id,
                            Type: z.TierCode,
                            Available: !string.Equals(seat.Status, "sold", StringComparison.OrdinalIgnoreCase),
                            Price: z.Price)).ToList())).ToList())))
                .ToList());
    }

    private static EventTicketDto ToTicketDto(EventTicket t) => new(
        Id: t.Id,
        Qr: t.Qr,
        EventId: t.EventId,
        AttendeeName: t.AttendeeName,
        Tier: t.Tier,
        Seat: t.Seat,
        HolderEmail: t.HolderEmail,
        Status: t.Status);

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

    public sealed record EventGeoDto(double Lat, double Lng);

    public sealed record EventSummaryDto(
        string Id,
        string Slug,
        string Title,
        string Category,
        string City,
        string Venue,
        DateTimeOffset StartUtc,
        // Contrato (EventSummary.startsAt): la UI lee `startsAt` (ISO) para la fecha
        // en cards/ficha/wallet. `startUtc` se conserva para consumers previos; ambos
        // portan el mismo instante.
        DateTimeOffset StartsAt,
        string ImageUrl,
        string Cover,
        decimal PriceFrom,
        decimal FromAmount,
        string PriceFromFormatted,
        string Currency,
        string Mode,
        // Contrato: la clave es "geo" (lat/lng del venue para el mapa/discovery),
        // null si el evento no tiene ubicación geocodificada.
        [property: JsonPropertyName("geo")] EventGeoDto? Geo,
        // Contrato UI (EventSummary): subtítulo compuesto (venue · ciudad), estado de
        // ciclo de vida derivado de la fecha, y chips freeform derivados del modo.
        string Subtitle,
        string Status,
        IReadOnlyList<string> Badges);

    public sealed record EventsResponse(IReadOnlyList<EventSummaryDto> Events);

    public sealed record EventTierDto(
        // Contrato: la UI lee `id` (el checkout manda tier.id). `code` se conserva
        // para consumers previos; ambos portan el código del tier.
        string Id,
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

    // Contrato UI (EventOrganizer): la ficha lee organizer.name (objeto).
    public sealed record EventOrganizerDto(string Name, string Headline, string Avatar);

    // ── Venue anidado (contrato UI EventVenue → VenueZone → SeatMapPayload) ──────
    // Es la forma que consume <synergos-seat-map>; distinta del EventSeatMapDto plano
    // de arriba (que se conserva por compat). rowNumber/available/type calcan las
    // claves exactas que lee la UI v2.

    public sealed record EventVenueSeatDto(
        string Id,
        // `type` (opcional en SeatMapSeat): se puebla con el tier de la zona
        // (price-level), útil para colorear el asiento por tier en el seat-map.
        string Type,
        bool Available,
        decimal Price);

    public sealed record EventVenueRowDto(
        // Contrato: la UI lee `rowNumber` (number|string); acá el label de la fila.
        [property: JsonPropertyName("rowNumber")] string RowNumber,
        IReadOnlyList<EventVenueSeatDto> Seats);

    public sealed record EventVenueSeatmapDto(IReadOnlyList<EventVenueRowDto> Rows);

    public sealed record EventVenueZoneDto(
        string Id,
        string Name,
        decimal Amount,
        // Contrato: la clave es "seatmap" (payload para <synergos-seat-map>).
        [property: JsonPropertyName("seatmap")] EventVenueSeatmapDto Seatmap);

    public sealed record EventVenueDto(
        string Name,
        string Address,
        string City,
        IReadOnlyList<EventVenueZoneDto> Zones);

    public sealed record EventDetailResponse(
        EventSummaryDto Event,
        string Description,
        // Contrato: la UI lee `organizer.name` (objeto {name,headline,avatar}), no un
        // string. headline/avatar no tienen fuente en EventDetail → cadena vacía.
        EventOrganizerDto Organizer,
        IReadOnlyList<EventTierDto> Tiers,
        // Contrato exacto: la clave es "seatmap" (todo minúscula), null en eventos
        // modo general. El default camelCase daría "seatMap"; lo fijamos al contrato.
        // Se conserva para consumers previos; la ficha v2 lee la forma anidada `venue`.
        [property: JsonPropertyName("seatmap")] EventSeatMapDto? SeatMap,
        // Contrato UI (EventVenue): la ficha lee `venue.zones[]` con la forma anidada
        // {id,name,amount,seatmap:{rows:[{rowNumber,seats:[{id,type,available,price}]}]}}.
        // null en eventos modo general (sin asientos numerados).
        [property: JsonPropertyName("venue")] EventVenueDto? Venue);

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
        string? Seat,
        string HolderEmail,
        string Status);

    public sealed record ConfirmResponse(string Status, IReadOnlyList<EventTicketDto> Tickets);

    // ── OLA 3 — Mis tickets / transferir / crear evento ─────────────────

    public sealed record TicketsResponse(IReadOnlyList<EventTicketDto> Tickets);

    public sealed record TransferRequest(string ToEmail);

    public sealed record TransferResponse(
        EventTicketDto Ticket,
        // Contrato exacto: la clave es "newQr" — el nuevo payload QR emitido tras
        // la transferencia (el QR viejo queda invalidado).
        [property: JsonPropertyName("newQr")] string NewQr);

    public sealed record EventTierDraftRequest(string Name, decimal Price, int Capacity);

    public sealed record EventDraftRequest(
        string Name,
        string? Venue,
        DateTimeOffset Date,
        IReadOnlyList<EventTierDraftRequest>? Tiers,
        string? City,
        string? Category,
        string? Currency,
        string? Description,
        string? Organizer,
        string? ImageUrl);

    public sealed record CreateEventResponse(string EventId);

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
