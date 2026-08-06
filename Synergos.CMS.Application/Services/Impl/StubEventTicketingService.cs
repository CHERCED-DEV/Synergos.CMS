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
/// (anti-tampering).
/// <see cref="ConfirmAsync"/> es idempotente: re-confirmar el mismo orderRef devuelve
/// los mismos tickets sin doble captura ni re-emisión.
/// <b>Este motor ya NO decide cómo se llama una entrada ni qué lleva su QR</b>: eso es
/// <see cref="EventTicketIssuer"/>, y está afuera para que un camino de compra que aparte
/// el aforo en otro sitio emita con el MISMO firmante y el MISMO formato en vez de copiarlo.
/// Acá quedó el CUÁNDO se emite (tras capturar y confirmar); allá vive el QUÉ se emite.
/// <b>T1/ADR 0105:</b> el estado (orderRef → <see cref="PersistedEventOrder"/>) ya NO
/// vive en un diccionario del proceso sino detrás del seam
/// <see cref="IJsonEntityStore"/> — con el adapter FileSystem la compra y sus tickets
/// SOBREVIVEN un reinicio del CMS. La cara de organizador
/// (<c>StubEventManagementService</c>) lo lee por composición (DIP) vía
/// <see cref="GetConfirmedTickets"/> / <see cref="MarkCheckedIn"/>. ADR 0075.
/// </remarks>
public sealed class StubEventTicketingService : IEventTicketingService
{
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
    private readonly ITransactionalNotifier? _notifier;

    /// <summary>El registro de las entradas: quién las tiene, quién ya entró. NO es de este motor.</summary>
    private readonly EventTicketLedger _ledger;
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
    /// <paramref name="ledger"/> es el registro COMPARTIDO de entradas emitidas; sin él se arma
    /// uno propio sobre <paramref name="store"/>.
    /// </summary>
    public StubEventTicketingService(
        IEventCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        IAuditTrailWriter? audit,
        IJsonEntityStore? store,
        Func<DateTimeOffset>? now,
        ITransactionalNotifier? notifier = null,
        ITicketSigner? signer = null,
        EventTicketLedger? ledger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _tracking = tracking;
        _notifier = notifier;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        // T9: sin firmante NO se emite QR ni se valida nada (fail-closed). Un QR sin firma sería
        // falsificable y —peor— parecería seguro. En Web siempre se cablea.
        // El registro se INYECTA cuando hay uno compartido: el motor de compra y la cara de
        // organizador tienen que mirar el mismo, o la puerta lee un almacén que nadie escribió.
        // Sin él se arma uno propio, que es lo correcto en un test que solo compra.
        _ledger = ledger ?? new EventTicketLedger(store, signer, audit, _now);
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

        await _ledger.SaveAsync(
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
        var order = await _ledger.LoadAsync(orderRef, cancellationToken);
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
            return _ledger.ConfirmationOf(order);
        }

        // 1) Capturar el pago de la orden completa (idempotente en el PSP).
        var capture = await _payments.CaptureAsync(order.PaymentSessionId, cancellationToken: cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            // No activar la compra ya estaba bien. Lo que faltaba era SOLTAR los
            // asientos: se quedaban apartados hasta que venciera el hold, y en
            // un evento con aforo eso son entradas que nadie más pudo comprar
            // por un pago que nunca ocurrió. Void libera además la retención de
            // fondos (ADR 0116 fase 1).
            await BestEffort.RunAsync(
                () => _payments.VoidAsync(order.PaymentSessionId, cancellationToken),
                cancellationToken);
            foreach (var unit in order.Units)
            {
                await BestEffort.RunAsync(
                    () => _reservations.CancelAsync(unit.ReservationId, "pago no capturado", cancellationToken),
                    cancellationToken);
            }

            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago de la orden (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas (ConfirmAsync idempotente por reserva).
        foreach (var unit in order.Units)
        {
            await _reservations.ConfirmAsync(unit.ReservationId, order.PaymentSessionId, cancellationToken);
        }

        var confirmed = order with { Status = EventOrderStatus.Confirmed };
        await _ledger.SaveAsync(confirmed, cancellationToken);

        // 3) La compra confirmada alimenta su timeline de tracking (seam genérico
        //    IOrderTrackingService): avanza a "confirmed" del pipeline
        //    paid→confirmed→attended (marca "paid" de paso, monotónico).
        //    AdvanceAsync es idempotente → un doble confirm no duplica la etapa.
        if (_tracking is not null)
        {
            // best-effort: la compra YA está confirmada y persistida.
            await BestEffort.RunAsync(() => _tracking.AdvanceAsync(
                order.OrderRef,
                StageConfirmed,
                $"Compra confirmada — {confirmed.Units.Count} ticket(s).",
                cancellationToken), cancellationToken);
        }

        // 4) Avisarle al COMPRADOR que sus entradas quedaron confirmadas (T4). Best-effort:
        //    un email caído JAMÁS puede tumbar una compra ya pagada y persistida.
        await EmitConfirmedAsync(confirmed, cancellationToken);

        return _ledger.ConfirmationOf(confirmed);
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
        => EventPurchaseNotification.EmitAsync(_notifier, order, _now(), cancellationToken);

    // ── Ciclo de vida del artefacto: NO es de este motor ────────────────
    // Mis entradas, transferir, la lista del organizador y la puerta viven en el registro
    // porque no dependen de por dónde se compró. Acá solo se reexponen para no romper al
    // llamador, que sigue hablándole al seam de siempre.

    public Task<IReadOnlyList<EventTicket>> GetTicketsAsync(string holderEmail, CancellationToken cancellationToken = default)
        => _ledger.TicketsOfAsync(holderEmail, cancellationToken);

    public Task<EventTicketTransferResult> TransferTicketAsync(
        string ticketId, string toEmail, CancellationToken cancellationToken = default)
        => _ledger.TransferAsync(ticketId, toEmail, cancellationToken);

    /// <summary>
    /// Los tickets confirmados de un evento. Firma sync PRESERVADA (call-sites intactos).
    /// </summary>
    public IReadOnlyList<EventAttendee> GetConfirmedTickets(string eventId)
        => GetConfirmedTicketsAsync(eventId).GetAwaiter().GetResult();

    /// <inheritdoc cref="GetConfirmedTickets"/>
    public Task<IReadOnlyList<EventAttendee>> GetConfirmedTicketsAsync(
        string eventId, CancellationToken cancellationToken = default)
        => _ledger.ConfirmedAttendeesAsync(eventId, cancellationToken);

    /// <summary>Marca un ticket como usado. Firma sync PRESERVADA.</summary>
    public string MarkCheckedIn(string ticketId)
        => MarkCheckedInAsync(ticketId).GetAwaiter().GetResult();

    /// <inheritdoc cref="MarkCheckedIn"/>
    public async Task<string> MarkCheckedInAsync(string ticketId, CancellationToken cancellationToken = default)
        => (await _ledger.CheckInAsync(ticketId, cancellationToken)).Status;

    /// <inheritdoc cref="MarkCheckedIn"/>
    public Task<EventCheckInResult> MarkCheckedInDetailedAsync(
        string ticketId, CancellationToken cancellationToken = default)
        => _ledger.CheckInAsync(ticketId, cancellationToken);

    /// <summary>Unidad de ticket EFÍMERA del cálculo de checkout — no se persiste.</summary>
    private sealed record PlannedUnit(string TierCode, string TierName, decimal Price, string? Seat);
}
