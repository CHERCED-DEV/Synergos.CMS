using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IEventTicketingService"/> — motor transaccional de la cara de
/// asistente del vertical Eventos (doc eventos-app-spec), calcando
/// <c>StubShopOrderService</c> de Tienda. Compone los seams existentes
/// (<see cref="IEventCatalogProvider"/> para resolver precio/aforo real,
/// <see cref="IReservationService"/> para el hold de cada asiento/cupo,
/// <see cref="IPaymentProvider"/> para el cobro) y lleva la compra por el flujo
/// unificado checkout → pagar (una sola vez) → confirmar (e-tickets QR).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO toca los flujos Booking/Travel/Shop (aditivo):
/// cada asiento/cupo es un RECURSO RESERVABLE POLIMÓRFICO (Event×Tier×Seat) apartado
/// con <see cref="IReservationService.HoldItemAsync"/> usando
/// <see cref="TravelProductType.Hotel"/> como discriminador neutro (Eventos no tiene
/// tipo propio en el enum del motor; la identidad real viaja en ProductRef/ProductLabel).
/// El precio NUNCA se confía al cliente: se resuelve desde el catálogo en checkout
/// (anti-tampering). El QR es un payload DETERMINISTA por ticket (hoy un string
/// estable <c>SYN-TKT-{eventId}-{ticketId}</c>; el adapter real lo firma con HMAC).
/// <see cref="ConfirmAsync"/> es idempotente: re-confirmar el mismo orderRef devuelve
/// los mismos tickets sin doble captura ni re-emisión. Estado en memoria del proceso;
/// la cara de organizador (<c>StubEventManagementService</c>) lo lee por composición
/// (DIP) vía <see cref="GetConfirmedTickets"/> / <see cref="MarkCheckedIn"/>. ADR 0075.
/// </remarks>
public sealed class StubEventTicketingService : IEventTicketingService
{
    private readonly IEventCatalogProvider _catalog;
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, OrderState> _orders = new(StringComparer.Ordinal);

    public StubEventTicketingService(
        IEventCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments)
        : this(catalog, reservations, payments, null)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable (<paramref name="now"/>) para
    /// determinismo en tests (ADR 0002: Application sin Umbraco). Null = reloj real.
    /// </summary>
    public StubEventTicketingService(
        IEventCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments,
        Func<DateTimeOffset>? now)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<EventCheckoutResult> CheckoutAsync(
        string eventId,
        IReadOnlyList<EventCheckoutItem> items,
        IReadOnlyList<EventAttendeeInfo> attendees,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("El evento es obligatorio.", nameof(eventId));
        }
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("El carrito requiere al menos un ticket.", nameof(items));
        }
        if (attendees is null || attendees.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos un asistente.", nameof(attendees));
        }

        var detail = await _catalog.GetEventAsync(eventId, cancellationToken)
            ?? throw new ArgumentException($"Evento '{eventId}' no encontrado.", nameof(eventId));

        // 1) Resolver precio/aforo REAL por línea desde el catálogo + expandir a
        //    unidades de ticket (una por asiento en reserved, qty en general).
        var plannedUnits = new List<PlannedUnit>();
        string? currency = null;

        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Tier))
            {
                throw new ArgumentException("Cada línea requiere un tier.", nameof(items));
            }

            var tier = detail.Tiers.FirstOrDefault(t =>
                string.Equals(t.Code, item.Tier, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"Tier '{item.Tier}' no existe para el evento '{detail.Summary.Id}'.", nameof(items));

            currency ??= tier.Currency;

            // Modo reserved: la línea trae un asiento → 1 unidad. Modo general:
            // la línea trae cantidad → N unidades del mismo tier.
            var hasSeat = !string.IsNullOrWhiteSpace(item.Seat);
            var qty = hasSeat ? 1 : item.Quantity;
            if (qty <= 0)
            {
                throw new ArgumentException("La cantidad de cada línea debe ser mayor a cero.", nameof(items));
            }
            if (qty > tier.Remaining)
            {
                throw new ArgumentException(
                    $"Aforo insuficiente para el tier '{tier.Name}' (quedan {tier.Remaining}, solicitado {qty}).", nameof(items));
            }
            if (qty > tier.MaxPerOrder)
            {
                throw new ArgumentException(
                    $"El tier '{tier.Name}' permite máximo {tier.MaxPerOrder} por orden (solicitado {qty}).", nameof(items));
            }

            for (var i = 0; i < qty; i++)
            {
                plannedUnits.Add(new PlannedUnit(tier.Code, tier.Name, tier.Price, hasSeat ? item.Seat!.Trim() : null));
            }
        }

        if (plannedUnits.Count != attendees.Count)
        {
            throw new ArgumentException(
                $"El número de asistentes ({attendees.Count}) debe igualar el número de tickets ({plannedUnits.Count}).",
                nameof(attendees));
        }

        // 2) Apartar cada unidad como una reserva (hold-timeout incluido) +
        //    armar las líneas de pago. El comprador es el primer asistente.
        var buyer = attendees[0];
        if (string.IsNullOrWhiteSpace(buyer.Name) || string.IsNullOrWhiteSpace(buyer.Email))
        {
            throw new ArgumentException("El nombre y el email del comprador son obligatorios.", nameof(attendees));
        }

        var units = new List<UnitState>(plannedUnits.Count);
        var paymentLines = new List<PaymentLineItem>(plannedUnits.Count);
        decimal total = 0m;

        for (var i = 0; i < plannedUnits.Count; i++)
        {
            var planned = plannedUnits[i];
            var attendee = attendees[i];
            var productRef = planned.Seat is null
                ? $"{detail.Summary.Id}/{planned.TierCode}"
                : $"{detail.Summary.Id}/{planned.TierCode}/{planned.Seat}";
            var label = planned.Seat is null
                ? $"{detail.Summary.Title} — {planned.TierName}"
                : $"{detail.Summary.Title} — {planned.TierName} (asiento {planned.Seat})";

            var reservation = await _reservations.HoldItemAsync(
                new TravelItemReservationRequest(
                    ProductType: TravelProductType.Hotel,
                    ProductRef: productRef,
                    ProductLabel: label,
                    GuestName: attendee.Name.Trim(),
                    GuestEmail: attendee.Email.Trim(),
                    TotalPrice: planned.Price,
                    Currency: currency!),
                cancellationToken);

            units.Add(new UnitState(
                planned.TierCode, planned.TierName, planned.Seat, planned.Price, currency!,
                attendee.Name.Trim(), attendee.Email.Trim(), attendee.DocumentId?.Trim(), reservation.Id));
            paymentLines.Add(new PaymentLineItem(
                Sku: productRef,
                Description: label,
                UnitPrice: planned.Price,
                Quantity: 1));
            total += planned.Price;
        }

        // 3) UNA sola sesión de pago por el total agregado de la orden.
        var orderRef = $"evord_{Guid.NewGuid():N}";
        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: orderRef,
                Amount: total,
                Currency: currency!,
                Items: paymentLines,
                CustomerEmail: buyer.Email.Trim(),
                ReturnUrl: null,
                Metadata: null),
            cancellationToken);

        _orders[orderRef] = new OrderState(
            OrderRef: orderRef,
            EventId: detail.Summary.Id,
            PaymentSessionId: session.SessionId,
            Total: total,
            Currency: currency!,
            Units: units,
            CreatedAt: _now());

        return new EventCheckoutResult(orderRef, session.SessionId, total, currency!);
    }

    public async Task<EventConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef) || !_orders.TryGetValue(orderRef, out var order))
        {
            throw new ArgumentException("Orden no encontrada.", nameof(orderRef));
        }

        // Idempotente: si ya está confirmada, devolver los mismos tickets sin
        // volver a capturar ni re-emitir.
        if (order.Status == EventOrderStatus.Confirmed)
        {
            return ToConfirmation(order);
        }

        // 1) Capturar el pago de la orden completa (idempotente en el PSP).
        var capture = await _payments.CaptureAsync(order.PaymentSessionId, cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago de la orden (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas (ConfirmAsync idempotente por reserva).
        foreach (var unit in order.Units)
        {
            await _reservations.ConfirmAsync(unit.ReservationId, order.PaymentSessionId, cancellationToken);
        }

        var confirmed = order with { Status = EventOrderStatus.Confirmed };
        _orders[orderRef] = confirmed;
        return ToConfirmation(confirmed);
    }

    // ── Lectura para la cara de organizador (StubEventManagementService) ──
    // Composición vía DIP: el motor de ticketing es la fuente de verdad de los
    // tickets confirmados; el management service los lee sin duplicar estado.

    /// <summary>
    /// Devuelve los tickets confirmados de un evento (uno por unidad) con su
    /// estado de check-in. Vacío si el evento no tiene órdenes confirmadas.
    /// </summary>
    public IReadOnlyList<EventAttendee> GetConfirmedTickets(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Array.Empty<EventAttendee>();
        }

        var id = eventId.Trim();
        return _orders.Values
            .Where(o => o.Status == EventOrderStatus.Confirmed
                && string.Equals(o.EventId, id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(o => o.Units)
            .Select(u => new EventAttendee(
                TicketId: TicketId(u.ReservationId),
                Name: u.AttendeeName,
                Email: u.AttendeeEmail,
                Tier: u.TierCode,
                Seat: u.Seat,
                CheckedIn: u.CheckedIn))
            .ToList();
    }

    /// <summary>
    /// Marca un ticket como usado (check-in). Idempotente: <c>valid</c> el primer
    /// check-in válido, <c>already-used</c> si ya estaba marcado, <c>invalid</c> si
    /// el ticket no corresponde a una unidad confirmada.
    /// </summary>
    public string MarkCheckedIn(string ticketId)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return "invalid";
        }

        var id = ticketId.Trim();
        foreach (var kvp in _orders)
        {
            var order = kvp.Value;
            if (order.Status != EventOrderStatus.Confirmed)
            {
                continue;
            }
            for (var i = 0; i < order.Units.Count; i++)
            {
                var unit = order.Units[i];
                if (!string.Equals(TicketId(unit.ReservationId), id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (unit.CheckedIn)
                {
                    return "already-used";
                }
                // Mutar la unidad in-place (record mutable via copy en la lista).
                var updatedUnits = order.Units.ToList();
                updatedUnits[i] = unit with { CheckedIn = true };
                _orders[kvp.Key] = order with { Units = updatedUnits };
                return "valid";
            }
        }
        return "invalid";
    }

    private EventConfirmationResult ToConfirmation(OrderState order)
    {
        var tickets = order.Units.Select(u => new EventTicket(
            Id: TicketId(u.ReservationId),
            Qr: BuildQr(order.EventId, TicketId(u.ReservationId)),
            EventId: order.EventId,
            AttendeeName: u.AttendeeName,
            Tier: u.TierCode,
            Seat: u.Seat)).ToList();
        return new EventConfirmationResult(order.Status.ToString(), tickets);
    }

    // Ticket id determinista derivado del id de la reserva (estable entre
    // confirmaciones de la misma orden → idempotencia del QR).
    private static string TicketId(string reservationId)
        => "tkt_" + reservationId.Replace("resv_", string.Empty, StringComparison.Ordinal);

    // Payload QR determinista por ticket. Hoy un string estable no-secreto; el
    // adapter real lo firma con HMAC server-side (spec §8 — anti-fraude).
    private static string BuildQr(string eventId, string ticketId)
        => $"SYN-TKT-{eventId}-{ticketId}";

    private sealed record PlannedUnit(string TierCode, string TierName, decimal Price, string? Seat);

    private sealed record UnitState(
        string TierCode,
        string TierName,
        string? Seat,
        decimal Price,
        string Currency,
        string AttendeeName,
        string AttendeeEmail,
        string? AttendeeDocument,
        string ReservationId)
    {
        public bool CheckedIn { get; init; }
    }

    private enum EventOrderStatus { Pending, Confirmed }

    private sealed record OrderState(
        string OrderRef,
        string EventId,
        string PaymentSessionId,
        decimal Total,
        string Currency,
        IReadOnlyList<UnitState> Units,
        DateTimeOffset CreatedAt)
    {
        public EventOrderStatus Status { get; init; } = EventOrderStatus.Pending;
    }
}
