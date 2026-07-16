using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ITravelCartService"/> — carrito de viaje multi-producto
/// server-side liviano. Es el MOTOR transaccional del dominio Booking: compone
/// los seams existentes (<see cref="IReservationService"/> +
/// <see cref="IPaymentProvider"/>) para llevar N ítems heterogéneos
/// (hotel|vuelo|auto) por el flujo unificado seleccionar → pagar (una sola vez)
/// → confirmar.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO toca el flujo hotel del BookingController
/// (aditivo): usa la vía polimórfica <see cref="IReservationService.HoldItemAsync"/>.
/// <b>Fan-out de T1 (doc 25):</b> el estado del carrito (orderRef → líneas + sesión de
/// pago + guest) ya NO vive en un diccionario del proceso sino detrás del seam
/// <see cref="ITravelOrderStore"/> — con un adapter FileSystem un carrito confirmado
/// SOBREVIVE un reinicio (las reservas y el pago ya lo hacían por T3).
/// <see cref="ConfirmAsync"/> es idempotente: re-confirmar el mismo orderRef devuelve el
/// mismo resultado sin doble captura ni doble efecto. ADR 0075 (seam con tests).
/// </remarks>
public sealed class TravelCartService : ITravelCartService
{
    // Estados agregados del carrito (JSON estable hacia la UI — doc 21 §2.2).
    private const string StatusPendingPayment = "PendingPayment";
    private const string StatusCancelled = "Cancelled";

    /// <summary>Etapa que siembra ConfirmAsync en el timeline de viaje.</summary>
    public const string StageConfirmed = "confirmed";

    /// <summary>
    /// Pipeline de tracking del dominio Booking (seam genérico
    /// <see cref="IOrderTrackingService"/>): pago → confirmado → próximo →
    /// completado. ConfirmAsync avanza a "confirmed" (marca "paid" de paso,
    /// monotónico); "upcoming"/"completed" los mueve la operación.
    /// </summary>
    public static readonly IReadOnlyList<OrderTrackingStageDefinition> TravelPipeline = new[]
    {
        new OrderTrackingStageDefinition("paid", "Pago confirmado"),
        new OrderTrackingStageDefinition(StageConfirmed, "Reserva confirmada"),
        new OrderTrackingStageDefinition("upcoming", "Próximo a viajar"),
        new OrderTrackingStageDefinition("completed", "Viaje completado"),
    };

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly IOrderTrackingService? _tracking;
    private readonly ITravelOrderStore _store;
    private readonly Func<DateTimeOffset> _now;

    public TravelCartService(IReservationService reservations, IPaymentProvider payments)
        : this(reservations, payments, null, null, null)
    {
    }

    /// <summary>
    /// Ctor con tracking + time source (OLA 2 Booking). Persistencia en memoria por
    /// default. <paramref name="tracking"/> opcional — si viene, la orden ALIMENTA su
    /// timeline de viaje al confirmar. <paramref name="now"/> = time source inyectable
    /// para tests (null = reloj real).
    /// </summary>
    public TravelCartService(
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        Func<DateTimeOffset>? now)
        : this(reservations, payments, tracking, now, null)
    {
    }

    /// <summary>
    /// Ctor completo (fan-out T1): <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests). Los ctors previos delegan con el default
    /// InMemory → cero call-sites rotos.
    /// </summary>
    public TravelCartService(
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        Func<DateTimeOffset>? now,
        ITravelOrderStore? store)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _tracking = tracking;
        _store = store ?? new InMemoryTravelOrderStore();
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<TravelCheckoutResult> CheckoutAsync(
        IReadOnlyList<TravelCartItem> items,
        TravelGuest guest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guest);
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("El carrito de viaje requiere al menos un ítem.", nameof(items));
        }
        if (string.IsNullOrWhiteSpace(guest.Name) || string.IsNullOrWhiteSpace(guest.Email))
        {
            throw new ArgumentException("El nombre y el email del viajero son obligatorios.", nameof(guest));
        }

        // Una sola moneda por carrito (el PSP cobra un total). Si llegan monedas
        // mezcladas es un error de armado del carrito: el split por moneda sería
        // sesiones de pago separadas, fuera del alcance del caso "un total".
        var currency = items[0].Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Cada ítem del carrito debe declarar su moneda.", nameof(items));
        }
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.OfferId) || string.IsNullOrWhiteSpace(item.Label))
            {
                throw new ArgumentException("Cada ítem requiere offerId y etiqueta.", nameof(items));
            }
            if (item.Price <= 0m)
            {
                throw new ArgumentException("El precio de cada ítem debe ser mayor a cero.", nameof(items));
            }
            if (!string.Equals(item.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Todos los ítems del carrito deben usar la misma moneda.", nameof(items));
            }
        }

        // 1) Reservar CADA ítem (un hold por ítem) vía la vía polimórfica.
        var lines = new List<CartLine>(items.Count);
        var paymentLines = new List<PaymentLineItem>(items.Count);
        decimal total = 0m;
        foreach (var item in items)
        {
            var reservation = await _reservations.HoldItemAsync(
                new TravelItemReservationRequest(
                    ProductType: item.Product,
                    ProductRef: item.OfferId,
                    ProductLabel: item.Label,
                    GuestName: guest.Name.Trim(),
                    GuestEmail: guest.Email.Trim(),
                    TotalPrice: item.Price,
                    Currency: currency),
                cancellationToken);

            lines.Add(new CartLine(item.Product, item.OfferId, item.Label, reservation.Id, item.Price, currency));
            paymentLines.Add(new PaymentLineItem(
                Sku: $"{item.Product}:{item.OfferId}",
                Description: item.Label,
                UnitPrice: item.Price,
                Quantity: 1));
            total += item.Price;
        }

        // 2) UNA sola sesión de pago por el total agregado del carrito.
        var orderRef = $"trip_{Guid.NewGuid():N}";
        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: orderRef,
                Amount: total,
                Currency: currency,
                Items: paymentLines,
                CustomerEmail: guest.Email.Trim(),
                ReturnUrl: null,
                Metadata: null),
            cancellationToken);

        // Registra el guest (email = clave de "Mis viajes") + fechas del ciclo.
        var createdAt = _now();
        var order = new CartOrder(
            orderRef, session.SessionId, total, currency, lines,
            guest.Name.Trim(), guest.Email.Trim(), createdAt, createdAt);
        await WriteAsync(order, cancellationToken);

        return new TravelCheckoutResult(orderRef, session.SessionId, total, currency);
    }

    public async Task<TravelConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderRef, cancellationToken);
        if (order is null)
        {
            throw new ArgumentException("Carrito de viaje no encontrado.", nameof(orderRef));
        }
        if (string.Equals(order.Status, StatusCancelled, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("El carrito de viaje ya fue cancelado.");
        }

        // 1) Capturar el pago del carrito completo (idempotente en el PSP).
        var capture = await _payments.CaptureAsync(order.PaymentSessionId, cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago del carrito (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas del carrito (ConfirmAsync es idempotente
        //    por reserva: re-confirmar deja Confirmed sin doble efecto). Si el hold
        //    de un ítem venció, ConfirmAsync lanza InvalidOperationException →
        //    burbujea como política de compensación a futuro (rollback/reembolso).
        var confirmedItems = new List<TravelOrderItem>(order.Lines.Count);
        foreach (var line in order.Lines)
        {
            var confirmed = await _reservations.ConfirmAsync(line.ReservationId, order.PaymentSessionId, cancellationToken);
            confirmedItems.Add(new TravelOrderItem(
                Product: line.Product,
                OfferId: line.OfferId,
                Label: line.Label,
                ReservationId: confirmed.Id,
                Status: confirmed.Status.ToString(),
                Price: line.Price,
                Currency: line.Currency));
        }

        var allConfirmed = confirmedItems.All(i =>
            string.Equals(i.Status, ReservationStatus.Confirmed.ToString(), StringComparison.Ordinal));
        var status = allConfirmed ? ReservationStatus.Confirmed.ToString() : "Partial";
        var confirmationCode = BuildConfirmationCode(order.OrderRef);

        // 3) Sella el estado agregado en la orden (para "Mis viajes"/MMB) y
        //    alimenta el timeline de viaje (paid→confirmed, monotónico; el
        //    AdvanceAsync es idempotente así que el re-confirm no duplica).
        await WriteAsync(order with
        {
            Status = status,
            ConfirmationCode = confirmationCode,
            UpdatedAt = _now(),
        }, cancellationToken);
        if (_tracking is not null && allConfirmed)
        {
            await _tracking.AdvanceAsync(
                order.OrderRef,
                StageConfirmed,
                $"Viaje confirmado — código {confirmationCode}.",
                cancellationToken);
        }

        return new TravelConfirmationResult(
            OrderRef: order.OrderRef,
            Status: status,
            ConfirmationCode: confirmationCode,
            Items: confirmedItems);
    }

    public async Task<IReadOnlyList<TravelOrder>> GetTripsAsync(string travelerEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(travelerEmail))
        {
            return Array.Empty<TravelOrder>();
        }

        var email = travelerEmail.Trim();
        var matches = (await LoadAllAsync(cancellationToken))
            .Where(o => string.Equals(o.GuestEmail, email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var trips = new List<TravelOrder>(matches.Count);
        foreach (var order in matches)
        {
            trips.Add(await ToTravelOrderAsync(order, cancellationToken));
        }
        return trips;
    }

    public async Task<TravelOrder?> GetOrderAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderRef, cancellationToken);
        return order is null ? null : await ToTravelOrderAsync(order, cancellationToken);
    }

    public async Task<TravelCancellationResult> CancelOrderAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderRef, cancellationToken);
        if (order is null)
        {
            throw new ArgumentException("Carrito de viaje no encontrado.", nameof(orderRef));
        }

        // Idempotente: re-cancelar devuelve el resultado sellado, sin doble
        // reembolso ni re-cancelación de reservas.
        if (string.Equals(order.Status, StatusCancelled, StringComparison.Ordinal))
        {
            return new TravelCancellationResult(
                order.OrderRef, order.Status, order.Refunded, order.RefundAmount, order.Currency);
        }

        // 1) Cancela CADA reserva del carrito (CancelAsync es idempotente por
        //    reserva en el motor).
        foreach (var line in order.Lines)
        {
            await _reservations.CancelAsync(
                line.ReservationId, $"Cancelación del viaje {order.OrderRef} (MMB).", cancellationToken);
        }

        // 2) Reembolso SOLO si el pago estaba capturado: el PSP devuelve
        //    Refunded únicamente sobre una sesión Captured (pre-confirm el stub
        //    responde con outcome no-Refunded y no hay nada que devolver).
        //    Nota: ICancellationPolicyEvaluator NO aplica aquí — evalúa por
        //    ratePlanCode+checkIn del vertical Hoteles (BookingController), y
        //    las líneas heterogéneas del carrito no cargan fechas/rate plan;
        //    el MMB v1 reembolsa total (política demo).
        var refunded = false;
        var refundAmount = 0m;
        var outcome = await _payments.RefundAsync(order.PaymentSessionId, null, cancellationToken);
        if (outcome.Status == PaymentStatus.Refunded)
        {
            refunded = true;
            refundAmount = outcome.AmountCaptured;
        }

        var cancelled = order with
        {
            Status = StatusCancelled,
            Refunded = refunded,
            RefundAmount = refundAmount,
            UpdatedAt = _now(),
        };
        await WriteAsync(cancelled, cancellationToken);

        return new TravelCancellationResult(
            cancelled.OrderRef, cancelled.Status, cancelled.Refunded, cancelled.RefundAmount, cancelled.Currency);
    }

    // Proyecta la orden interna a TravelOrder leyendo el estado de cada ítem
    // EN VIVO del motor de reservas (fuente de verdad) + la etapa del timeline.
    private async Task<TravelOrder> ToTravelOrderAsync(CartOrder order, CancellationToken cancellationToken)
    {
        var items = new List<TravelOrderItem>(order.Lines.Count);
        foreach (var line in order.Lines)
        {
            var reservation = await _reservations.GetAsync(line.ReservationId, cancellationToken);
            items.Add(new TravelOrderItem(
                Product: line.Product,
                OfferId: line.OfferId,
                Label: line.Label,
                ReservationId: line.ReservationId,
                Status: reservation?.Status.ToString() ?? ReservationStatus.Held.ToString(),
                Price: line.Price,
                Currency: line.Currency));
        }

        string? currentStage = null;
        if (_tracking is not null)
        {
            currentStage = (await _tracking.GetTimelineAsync(order.OrderRef, cancellationToken))?.CurrentStage;
        }

        return new TravelOrder(
            OrderRef: order.OrderRef,
            Status: order.Status,
            ConfirmationCode: order.ConfirmationCode,
            GuestName: order.GuestName,
            GuestEmail: order.GuestEmail,
            Items: items,
            Total: order.Total,
            Currency: order.Currency,
            CreatedAt: order.CreatedAt,
            UpdatedAt: order.UpdatedAt,
            CurrentStage: currentStage);
    }

    // ── Carga/escritura desde el store (deserialización defensiva) ──────
    private async Task<CartOrder?> LoadAsync(string? orderRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return null;
        }
        var json = await _store.ReadAsync(orderRef.Trim(), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<CartOrder>(json, _json); }
        catch (JsonException) { return null; }   // corrupto → como si no existiera
    }

    private async Task<List<CartOrder>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var orders = new List<CartOrder>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            CartOrder? order;
            try { order = JsonSerializer.Deserialize<CartOrder>(json, _json); }
            catch (JsonException) { continue; }
            if (order is not null) orders.Add(order);
        }
        return orders;
    }

    private Task WriteAsync(CartOrder order, CancellationToken cancellationToken)
        => _store.WriteAsync(order.OrderRef, JsonSerializer.Serialize(order, _json), cancellationToken);

    // Código de confirmación human-facing derivado determinísticamente del
    // orderRef (idempotente: re-confirmar el mismo orderRef da el mismo código).
    private static string BuildConfirmationCode(string orderRef)
        => "SYN-" + orderRef.Replace("trip_", string.Empty, StringComparison.Ordinal)[..8].ToUpperInvariant();
}

/// <summary>Una línea del carrito de viaje (ítem reservado). Promovida a top-level
/// internal para round-trip limpio con System.Text.Json (fan-out T1).</summary>
internal readonly record struct CartLine(
    TravelProductType Product,
    string OfferId,
    string Label,
    string ReservationId,
    decimal Price,
    string Currency);

/// <summary>El estado serializado de una orden de viaje (el superset que persiste el
/// motor). Promovida a top-level internal para round-trip limpio con System.Text.Json.</summary>
internal sealed record CartOrder(
    string OrderRef,
    string PaymentSessionId,
    decimal Total,
    string Currency,
    IReadOnlyList<CartLine> Lines,
    string GuestName,
    string GuestEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string Status { get; init; } = "PendingPayment";
    public string? ConfirmationCode { get; init; }
    public bool Refunded { get; init; }
    public decimal RefundAmount { get; init; }
}
