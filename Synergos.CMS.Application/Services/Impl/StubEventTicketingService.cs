using System.Text.Encodings.Web;
using System.Text.Json;
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
/// los mismos tickets sin doble captura ni re-emisión.
/// <b>T1/ADR 0105:</b> el estado (orderRef → <see cref="PersistedEventOrder"/>) ya NO
/// vive en un diccionario del proceso sino detrás del seam
/// <see cref="IJsonEntityStore"/> — con el adapter FileSystem la compra y sus tickets
/// SOBREVIVEN un reinicio del CMS. La cara de organizador
/// (<c>StubEventManagementService</c>) lo lee por composición (DIP) vía
/// <see cref="GetConfirmedTickets"/> / <see cref="MarkCheckedIn"/>. ADR 0075.
/// </remarks>
public sealed class StubEventTicketingService : IEventTicketingService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    /// <summary>Familia de entidades de este motor en el store genérico (→ App_Data/syn-event-orders/).</summary>
    private const string ResourceType = "event-orders";

    /// <summary>Etapa que siembra ConfirmAsync en el timeline de eventos.</summary>
    public const string StageConfirmed = "confirmed";

    /// <summary>
    /// Pipeline de tracking del dominio Eventos (seam genérico
    /// <see cref="IOrderTrackingService"/>): pago → confirmado → asistió.
    /// ConfirmAsync avanza a "confirmed" (marca "paid" de paso, monotónico); la
    /// etapa "attended" la mueve el check-in / la operación del evento.
    /// </summary>
    public static readonly IReadOnlyList<OrderTrackingStageDefinition> EventPipeline = new[]
    {
        new OrderTrackingStageDefinition("paid", "Pago confirmado"),
        new OrderTrackingStageDefinition(StageConfirmed, "Compra confirmada"),
        new OrderTrackingStageDefinition("attended", "Asistió al evento"),
    };

    private readonly IEventCatalogProvider _catalog;
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly IOrderTrackingService? _tracking;
    private readonly IAuditTrailWriter? _audit;
    private readonly IJsonEntityStore _store;
    private readonly ITransactionalNotifier? _notifier;
    private readonly Func<DateTimeOffset> _now;

    public StubEventTicketingService(
        IEventCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments)
        : this(catalog, reservations, payments, null, null, null, null)
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
        : this(catalog, reservations, payments, null, null, null, now)
    {
    }

    /// <summary>
    /// Ctor de OLA 3 Eventos: <paramref name="tracking"/> opcional — si viene, la
    /// compra ALIMENTA su timeline al confirmar (avanza a "confirmed" del
    /// <see cref="EventPipeline"/>; construir el tracker con ese pipeline).
    /// <paramref name="audit"/> opcional — si viene, cada transferencia de ticket se
    /// asienta append-only (<see cref="IAuditTrailWriter"/>). Todos null ≡ ctor
    /// original (aditivo). Persistencia en memoria por default.
    /// </summary>
    public StubEventTicketingService(
        IEventCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now)
        : this(catalog, reservations, payments, tracking, audit, null, now)
    {
    }

    /// <summary>
    /// Ctor completo (T1 durabilidad, ADR 0105): <paramref name="store"/> es el backing
    /// store de la orden (FileSystem en Web → sobrevive un reinicio; InMemory en tests).
    /// Null ≡ <see cref="InMemoryJsonEntityStore"/> (comportamiento previo, aditivo).
    /// <paramref name="notifier"/> opcional (T4): si viene, la compra confirmada le avisa
    /// al COMPRADOR (un solo email con todas las entradas). Null ≡ no notificar.
    /// </summary>
    public StubEventTicketingService(
        IEventCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        IAuditTrailWriter? audit,
        IJsonEntityStore? store,
        Func<DateTimeOffset>? now,
        ITransactionalNotifier? notifier = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _tracking = tracking;
        _audit = audit;
        _store = store ?? new InMemoryJsonEntityStore();
        _notifier = notifier;
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

        var units = new List<PersistedEventUnit>(plannedUnits.Count);
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

            units.Add(new PersistedEventUnit(
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

        await WriteAsync(
            new PersistedEventOrder(
                OrderRef: orderRef,
                EventId: detail.Summary.Id,
                PaymentSessionId: session.SessionId,
                Total: total,
                Currency: currency!,
                Units: units,
                CreatedAt: _now()),
            cancellationToken);

        return new EventCheckoutResult(orderRef, session.SessionId, total, currency!);
    }

    public async Task<EventConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderRef, cancellationToken);
        if (order is null)
        {
            throw new ArgumentException("Orden no encontrada.", nameof(orderRef));
        }

        // Idempotente: si ya está confirmada, devolver los mismos tickets sin
        // volver a capturar ni re-emitir.
        if (order.Status == EventOrderStatus.Confirmed)
        {
            // Re-emite: el ledger del dispatcher deduplica (un hecho → un aviso), así que
            // es inofensivo, y rescata el caso en que el primer confirm no llegó a
            // notificar (notificaciones apagadas entonces, destinatario inválido, etc.).
            await EmitConfirmedAsync(order, cancellationToken);
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
        await WriteAsync(confirmed, cancellationToken);

        // 3) La compra confirmada alimenta su timeline de tracking (seam genérico
        //    IOrderTrackingService): avanza a "confirmed" del pipeline
        //    paid→confirmed→attended (marca "paid" de paso, monotónico).
        //    AdvanceAsync es idempotente → un doble confirm no duplica la etapa.
        if (_tracking is not null)
        {
            await _tracking.AdvanceAsync(
                order.OrderRef,
                StageConfirmed,
                $"Compra confirmada — {confirmed.Units.Count} ticket(s).",
                cancellationToken);
        }

        // 4) Avisarle al COMPRADOR que sus entradas quedaron confirmadas (T4). Best-effort:
        //    un email caído JAMÁS puede tumbar una compra ya pagada y persistida.
        await EmitConfirmedAsync(confirmed, cancellationToken);

        return ToConfirmation(confirmed);
    }

    /// <summary>
    /// Emite el hecho "entradas confirmadas" (T4) — UN SOLO email al COMPRADOR con TODAS
    /// las entradas, nunca uno por asistente.
    /// </summary>
    /// <remarks>
    /// Razón dura: el motor solo VALIDA el email del comprador (attendees[0] en
    /// <see cref="CheckoutAsync"/>); a los demás asistentes apenas les hace Trim(), así que
    /// notificar por-asistente dispararía contra strings vacíos. El comprador es la unidad
    /// original (<c>AttendeeEmail</c>, no <c>HolderEmail</c>: transferir un ticket cambia el
    /// portador, no a quién le confirmamos la compra). Si el destinatario persistido no es
    /// usable, NO se emite basura — el dispatcher filtra inválidos, pero no le inventamos
    /// un placeholder.
    /// </remarks>
    private Task EmitConfirmedAsync(PersistedEventOrder order, CancellationToken cancellationToken)
    {
        var notification = BuildConfirmedNotification(order);
        return notification is null
            ? Task.CompletedTask
            : NotificationEmission.SafeDispatchAsync(_notifier, notification, cancellationToken);
    }

    /// <summary>El hecho "entradas confirmadas". DedupeKey default = events.tickets.confirmed:{orderRef}
    /// — el orderRef identifica el hecho, así que no hace falta override.</summary>
    private NotificationEvent? BuildConfirmedNotification(PersistedEventOrder order)
    {
        var buyer = order.Units.Count > 0 ? order.Units[0] : null;
        if (buyer is null
            || string.IsNullOrWhiteSpace(buyer.AttendeeEmail)
            || string.IsNullOrWhiteSpace(buyer.AttendeeName))
        {
            return null;   // sin destinatario usable no se emite (nada de placeholders)
        }

        return new NotificationEvent(
            Type: NotificationTypes.EventTicketsConfirmed,
            SubjectId: order.OrderRef,
            ToEmail: buyer.AttendeeEmail,
            ToName: buyer.AttendeeName,
            Code: BuildOrderNumber(order.OrderRef),
            OccurredAt: _now(),
            Amount: order.Total,
            Currency: order.Currency,
            Lines: order.Units
                .Select(u => new NotificationLine(
                    Label: u.Seat is null ? u.TierName : $"{u.TierName} (asiento {u.Seat})",
                    Quantity: 1,
                    Amount: u.Price,
                    Currency: u.Currency,
                    Detail: u.Seat ?? TicketId(u.ReservationId)))
                .ToList(),
            ActionPath: $"/eventos/entradas/{order.OrderRef}");
    }

    public async Task<IReadOnlyList<EventTicket>> GetTicketsAsync(string holderEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(holderEmail))
        {
            return Array.Empty<EventTicket>();
        }

        var email = holderEmail.Trim();
        var all = await LoadAllAsync(cancellationToken);
        return all
            .Where(o => o.Status == EventOrderStatus.Confirmed)
            .OrderBy(o => o.CreatedAt)
            .SelectMany(o => o.Units.Select(u => (o.EventId, Unit: u)))
            .Where(x => string.Equals(x.Unit.HolderEmail, email, StringComparison.OrdinalIgnoreCase))
            .Select(x => ToTicket(x.EventId, x.Unit))
            .ToList();
    }

    public async Task<EventTicketTransferResult> TransferTicketAsync(
        string ticketId,
        string toEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            throw new ArgumentException("El ticket es obligatorio.", nameof(ticketId));
        }
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("El email de destino es inválido.", nameof(toEmail));
        }

        var id = ticketId.Trim();
        var newEmail = toEmail.Trim();

        // Localizar la unidad confirmada correspondiente al ticket (read-modify-write
        // sobre la orden dueña; el resto de órdenes no se toca).
        var all = await LoadAllAsync(cancellationToken);
        foreach (var order in all.OrderBy(o => o.CreatedAt))
        {
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

                // Ya usado/cancelado → no transferible.
                if (unit.CheckedIn)
                {
                    throw new ArgumentException("El ticket ya fue usado y no puede transferirse.", nameof(ticketId));
                }

                // Idempotente: transferir al portador actual no rota ni re-audita.
                if (string.Equals(unit.HolderEmail, newEmail, StringComparison.OrdinalIgnoreCase))
                {
                    var same = ToTicket(order.EventId, unit);
                    return new EventTicketTransferResult(same, same.Qr);
                }

                // Reasignar holder + rotar el QR (bump de versión ⇒ QR viejo inválido).
                var derivedName = DeriveNameFromEmail(newEmail);
                var updatedUnit = unit with
                {
                    HolderEmail = newEmail,
                    HolderName = derivedName,
                    QrVersion = unit.QrVersion + 1,
                };
                var updatedUnits = order.Units.ToList();
                updatedUnits[i] = updatedUnit;
                await WriteAsync(order with { Units = updatedUnits }, cancellationToken);

                var ticket = ToTicket(order.EventId, updatedUnit);

                // Auditar la transferencia (append-only, ADR 0037).
                if (_audit is not null)
                {
                    await _audit.WriteAsync(
                        new AuditEvent(
                            Id: Guid.NewGuid().ToString("N"),
                            OccurredAtUtc: _now().UtcDateTime,
                            ActorEmail: unit.HolderEmail,
                            ActorName: unit.HolderName,
                            Action: "event.ticket.transfer",
                            Resource: $"{order.EventId}/{id}",
                            Outcome: "success",
                            Detail: $"Ticket transferido de '{unit.HolderEmail}' a '{newEmail}'; QR rotado a v{updatedUnit.QrVersion}."),
                        cancellationToken);
                }

                return new EventTicketTransferResult(ticket, ticket.Qr);
            }
        }

        throw new ArgumentException($"Ticket '{ticketId}' no encontrado.", nameof(ticketId));
    }

    // Deriva un nombre razonable del email del nuevo portador (la parte local,
    // capitalizada). El adapter real resolvería el nombre del Member/cuenta.
    private static string DeriveNameFromEmail(string email)
    {
        var local = email.Split('@', 2)[0].Replace('.', ' ').Replace('_', ' ').Trim();
        if (string.IsNullOrEmpty(local))
        {
            return email;
        }
        return char.ToUpperInvariant(local[0]) + local[1..];
    }

    // ── Lectura para la cara de organizador (StubEventManagementService) ──
    // Composición vía DIP: el motor de ticketing es la fuente de verdad de los
    // tickets confirmados; el management service los lee sin duplicar estado.

    /// <summary>
    /// Devuelve los tickets confirmados de un evento (uno por unidad) con su
    /// estado de check-in. Vacío si el evento no tiene órdenes confirmadas.
    /// </summary>
    /// <remarks>
    /// Firma sync PRESERVADA (call-sites intactos) sobre el store async: envuelve
    /// <see cref="GetConfirmedTicketsAsync"/>. Preferir la variante async en código nuevo.
    /// </remarks>
    public IReadOnlyList<EventAttendee> GetConfirmedTickets(string eventId)
        => GetConfirmedTicketsAsync(eventId).GetAwaiter().GetResult();

    /// <inheritdoc cref="GetConfirmedTickets"/>
    public async Task<IReadOnlyList<EventAttendee>> GetConfirmedTicketsAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Array.Empty<EventAttendee>();
        }

        var id = eventId.Trim();
        var all = await LoadAllAsync(cancellationToken);
        return all
            .Where(o => o.Status == EventOrderStatus.Confirmed
                && string.Equals(o.EventId, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.CreatedAt)
            .SelectMany(o => o.Units)
            .Select(u => new EventAttendee(
                TicketId: TicketId(u.ReservationId),
                Name: u.HolderName,
                Email: u.HolderEmail,
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
    /// <remarks>
    /// Firma sync PRESERVADA (call-sites intactos) sobre el store async: envuelve
    /// <see cref="MarkCheckedInAsync"/>. Preferir la variante async en código nuevo.
    /// </remarks>
    public string MarkCheckedIn(string ticketId)
        => MarkCheckedInAsync(ticketId).GetAwaiter().GetResult();

    /// <inheritdoc cref="MarkCheckedIn"/>
    public async Task<string> MarkCheckedInAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return "invalid";
        }

        var id = ticketId.Trim();
        var all = await LoadAllAsync(cancellationToken);
        foreach (var order in all.OrderBy(o => o.CreatedAt))
        {
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
                await WriteAsync(order with { Units = updatedUnits }, cancellationToken);
                return "valid";
            }
        }
        return "invalid";
    }

    // ── Carga/escritura del store (deserialización defensiva) ───────────
    private async Task<PersistedEventOrder?> LoadAsync(string? orderRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return null;
        }
        var json = await _store.ReadAsync(ResourceType, orderRef.Trim(), cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<PersistedEventOrder>(json, _json); }
        catch (JsonException) { return null; }   // archivo corrupto → como si no existiera
    }

    private async Task<List<PersistedEventOrder>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(ResourceType, cancellationToken);
        var orders = new List<PersistedEventOrder>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            PersistedEventOrder? order;
            try { order = JsonSerializer.Deserialize<PersistedEventOrder>(json, _json); }
            catch (JsonException) { continue; }
            if (order is not null) orders.Add(order);
        }
        return orders;
    }

    private Task WriteAsync(PersistedEventOrder order, CancellationToken cancellationToken)
        => _store.WriteAsync(ResourceType, order.OrderRef, JsonSerializer.Serialize(order, _json), cancellationToken);

    private static EventConfirmationResult ToConfirmation(PersistedEventOrder order)
    {
        var tickets = order.Units.Select(u => ToTicket(order.EventId, u)).ToList();
        return new EventConfirmationResult(order.Status.ToString(), tickets);
    }

    // Proyecta una unidad confirmada a su e-ticket (con QR rotativo + holder + estado).
    private static EventTicket ToTicket(string eventId, PersistedEventUnit u)
    {
        var ticketId = TicketId(u.ReservationId);
        return new EventTicket(
            Id: ticketId,
            Qr: BuildQr(eventId, ticketId, u.HolderEmail, u.QrVersion),
            EventId: eventId,
            AttendeeName: u.HolderName,
            Tier: u.TierCode,
            Seat: u.Seat,
            HolderEmail: u.HolderEmail,
            Status: TicketStatus(u));
    }

    // Estado del ticket para la cara de asistente: used (ya escaneado) tiene
    // prioridad; luego transferred (QR rotado al menos una vez); si no, valid.
    private static string TicketStatus(PersistedEventUnit u)
    {
        if (u.CheckedIn)
        {
            return "used";
        }
        return u.QrVersion > 0 ? "transferred" : "valid";
    }

    // Ticket id determinista derivado del id de la reserva (estable entre
    // confirmaciones de la misma orden → idempotencia del ticket).
    private static string TicketId(string reservationId)
        => "tkt_" + reservationId.Replace("resv_", string.Empty, StringComparison.Ordinal);

    // Payload QR ROTATIVO determinista por ticket (SafeTix-like): incluye el holder
    // + la versión, así que transferir (bump de versión) INVALIDA el QR viejo y
    // emite uno nuevo. Hoy un string estable no-secreto; el adapter real lo firma
    // con HMAC server-side (spec §8 — anti-fraude/anti-reventa).
    private static string BuildQr(string eventId, string ticketId, string holderEmail, int qrVersion)
    {
        var holderTag = Math.Abs(
            StringComparer.OrdinalIgnoreCase.GetHashCode(holderEmail ?? string.Empty))
            .ToString("X8");
        return $"SYN-TKT-{eventId}-{ticketId}-v{qrVersion}-{holderTag}";
    }

    // Número de orden human-facing derivado determinísticamente del orderRef (calcando
    // StubShopOrderService): re-confirmar el mismo orderRef da el mismo número, así que
    // el código que el asistente guarda es estable entre confirmaciones y reinicios.
    private static string BuildOrderNumber(string orderRef)
    {
        var raw = orderRef.Replace("evord_", string.Empty, StringComparison.Ordinal);
        return "SYN-EVT-" + (raw.Length >= 8 ? raw[..8] : raw).ToUpperInvariant();
    }

    /// <summary>Unidad de ticket EFÍMERA del cálculo de checkout — no se persiste.</summary>
    private sealed record PlannedUnit(string TierCode, string TierName, decimal Price, string? Seat);
}

/// <summary>Estado de una compra de tickets. Serializado como número en el store.</summary>
internal enum EventOrderStatus { Pending, Confirmed }

/// <summary>
/// La forma SERIALIZADA de una unidad de ticket (el antiguo <c>UnitState</c> anidado del
/// <see cref="StubEventTicketingService"/>, promovido a top-level para round-trip limpio
/// con System.Text.Json). Guarda MÁS que el <see cref="EventTicket"/> público:
/// <see cref="ReservationId"/> es necesario para que <c>ConfirmAsync</c> sobreviva un
/// reinicio, y <see cref="QrVersion"/>/<see cref="CheckedIn"/> son el estado anti-reventa
/// y anti-doble-entrada.
/// </summary>
internal sealed record PersistedEventUnit(
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

    /// <summary>Email del portador ACTUAL del ticket (cambia al transferir).</summary>
    public string HolderEmail { get; init; } = AttendeeEmail;

    /// <summary>Nombre del portador actual (cambia al transferir).</summary>
    public string HolderName { get; init; } = AttendeeName;

    /// <summary>
    /// Versión del QR — arranca en 0 y se incrementa en cada transferencia
    /// (SafeTix-like: el QR es determinista por holder+ticket+versión, así que
    /// bumpear la versión INVALIDA el QR viejo y emite uno nuevo).
    /// </summary>
    public int QrVersion { get; init; }
}

/// <summary>
/// La forma SERIALIZADA de una compra de tickets (el antiguo <c>OrderState</c> anidado del
/// <see cref="StubEventTicketingService"/>, promovido a top-level para round-trip limpio
/// con System.Text.Json). <see cref="PaymentSessionId"/> es necesario para que
/// <c>ConfirmAsync</c> sobreviva un reinicio del CMS (T1/ADR 0105).
/// </summary>
internal sealed record PersistedEventOrder(
    string OrderRef,
    string EventId,
    string PaymentSessionId,
    decimal Total,
    string Currency,
    IReadOnlyList<PersistedEventUnit> Units,
    DateTimeOffset CreatedAt)
{
    public EventOrderStatus Status { get; init; } = EventOrderStatus.Pending;
}
