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
/// <see cref="IJsonEntityStore"/> — con un adapter FileSystem un carrito confirmado
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
    /// <summary>Familia de entidades de este motor en el store genérico (→ App_Data/syn-travel-orders/).</summary>
    private const string ResourceType = "travel-orders";

    private readonly IJsonEntityStore _store;
    private readonly ITransactionalNotifier? _notifier;
    private readonly IAuditTrailWriter? _audit;
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
    /// Ctor completo (fan-out T1). Los seams opcionales van al final y con default, para no
    /// re-ligar los args posicionales de los factories del composer cada vez que aparece uno.
    /// </summary>
    /// <param name="reservations">Motor de reservas: aparta y libera cada línea del carrito.</param>
    /// <param name="payments">Motor de pagos: una sesión por carrito, un total.</param>
    /// <param name="tracking">
    /// Timeline del viaje. Si viene, la orden lo alimenta al confirmar.
    /// </param>
    /// <param name="now">Reloj inyectable para los tests. Null usa el del sistema.</param>
    /// <param name="store">
    /// Backing store durable (FileSystem en Web, InMemory en tests). Null deja el carrito en
    /// memoria, que es lo que hacen los ctors previos.
    /// </param>
    /// <param name="notifier">Aviso transaccional al viajero (T4). Opcional.</param>
    /// <param name="audit">
    /// Rastro de la cancelación (ADR 0037). Sin él la cancelación sigue funcionando y no queda
    /// registrada — que es exactamente el hueco que este parámetro cierra.
    /// </param>
    public TravelCartService(
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store,
        ITransactionalNotifier? notifier = null,
        IAuditTrailWriter? audit = null)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _tracking = tracking;
        _store = store ?? new InMemoryJsonEntityStore();
        _notifier = notifier;
        _audit = audit;
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

            // El periodo se rechaza ACÁ, en el borde, antes de tocar ninguna capacidad
            // (HU #40). `Bff.Viajes` ya lo rechaza con `viajes.bad_window`, pero
            // descubrirlo allá cuesta un viaje de ida y vuelta por algo que se sabe
            // de este lado. Es la misma familia que las cuatro validaciones de arriba.
            if (item.End <= item.Start)
            {
                throw new ArgumentException(
                    "El periodo de cada ítem debe avanzar: la fecha de fin tiene que ser posterior a la de inicio.",
                    nameof(items));
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
        var capture = await _payments.CaptureAsync(order.PaymentSessionId, cancellationToken: cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago del carrito (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas del carrito (ConfirmAsync es idempotente
        //    por reserva: re-confirmar deja Confirmed sin doble efecto).
        //
        //    Un hold vencido hacía que ConfirmAsync lanzara y el comentario
        //    original lo dejaba "burbujear como política de compensación a
        //    futuro". Ese futuro es ahora: si un ítem no se pudo honrar, el
        //    cliente YA pagó el carrito entero y hay que devolverle esa parte.
        //    Sin esto, la orden quedaba en "Partial" y el dinero de lo no
        //    entregado se quedaba acá.
        var confirmedItems = new List<TravelOrderItem>(order.Lines.Count);
        var unfulfilledAmount = 0m;
        foreach (var line in order.Lines)
        {
            string lineStatus;
            string reservationId = line.ReservationId;
            try
            {
                var confirmed = await _reservations.ConfirmAsync(
                    line.ReservationId, order.PaymentSessionId, cancellationToken);
                lineStatus = confirmed.Status.ToString();
                reservationId = confirmed.Id;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Un ítem que no se pudo confirmar NO tumba los que sí: el
                // cliente se queda con el vuelo aunque el hotel se haya caído,
                // y se le devuelve lo del hotel. Cancelar todo sería peor
                // servicio y más plata moviéndose sin necesidad.
                lineStatus = ReservationStatus.Cancelled.ToString();
            }

            if (!string.Equals(lineStatus, ReservationStatus.Confirmed.ToString(), StringComparison.Ordinal))
            {
                unfulfilledAmount += line.Price;
            }

            confirmedItems.Add(new TravelOrderItem(
                Product: line.Product,
                OfferId: line.OfferId,
                Label: line.Label,
                ReservationId: reservationId,
                Status: lineStatus,
                Price: line.Price,
                Currency: line.Currency));
        }

        var allConfirmed = unfulfilledAmount == 0m;
        var noneConfirmed = confirmedItems.Count > 0 && unfulfilledAmount >= order.Total;

        // 3) Devolver lo que no se pudo entregar. Reembolso PARCIAL por el monto
        //    de las líneas caídas — para eso RefundAsync acepta monto.
        //    Best-effort: lo que SÍ salió ya está confirmado, y un reembolso
        //    caído no puede deshacer un viaje confirmado. Pero se intenta
        //    siempre, porque no intentarlo es el defecto que esto cierra.
        if (unfulfilledAmount > 0m && !string.IsNullOrWhiteSpace(order.PaymentSessionId))
        {
            await BestEffort.RunAsync(
                () => _payments.RefundAsync(order.PaymentSessionId, unfulfilledAmount, cancellationToken),
                cancellationToken);
        }

        // 4) Si NADA se pudo confirmar, el confirm falló: se devuelve todo y se
        //    lanza. Un carrito donde no sobrevivió ni un ítem no es un viaje
        //    "parcial", es un viaje que no existe, y el llamante tiene que
        //    enterarse. El caso PARCIAL —parte confirmada, parte devuelta— sí
        //    es un resultado legítimo y se reporta como tal.
        if (noneConfirmed)
        {
            var cancelled = order with
            {
                Status = StatusCancelled,
                UpdatedAt = _now(),
            };
            await WriteAsync(cancelled, cancellationToken);

            throw new InvalidOperationException(
                "Ningún ítem del carrito se pudo confirmar; el pago fue devuelto en su totalidad.");
        }

        var status = allConfirmed ? ReservationStatus.Confirmed.ToString() : "Partial";
        var confirmationCode = BuildConfirmationCode(order.OrderRef);

        // 3) Sella el estado agregado en la orden (para "Mis viajes"/MMB) y
        //    alimenta el timeline de viaje (paid→confirmed, monotónico; el
        //    AdvanceAsync es idempotente así que el re-confirm no duplica).
        var confirmedOrder = order with
        {
            Status = status,
            ConfirmationCode = confirmationCode,
            UpdatedAt = _now(),
        };
        await WriteAsync(confirmedOrder, cancellationToken);
        if (_tracking is not null && allConfirmed)
        {
            // best-effort: el viaje YA está confirmado y persistido.
            await BestEffort.RunAsync(() => _tracking.AdvanceAsync(
                order.OrderRef,
                StageConfirmed,
                $"Viaje confirmado — código {confirmationCode}.",
                cancellationToken), cancellationToken);
        }

        // 4) Avisarle al viajero (T4). A diferencia del tracking, esto va FUERA del gate
        //    allConfirmed: si el viaje quedó 'Partial' el viajero DEBE enterarse —
        //    callarse justo cuando quedó a medias es lo peor que puede hacer el sistema.
        //    Best-effort: un email caído JAMÁS puede tumbar un viaje ya pagado y sellado.
        await NotificationEmission.SafeDispatchAsync(
            _notifier, BuildConfirmedNotification(confirmedOrder, confirmedItems), cancellationToken);

        return new TravelConfirmationResult(
            OrderRef: order.OrderRef,
            Status: status,
            ConfirmationCode: confirmationCode,
            Items: confirmedItems);
    }

    /// <summary>
    /// El hecho "viaje confirmado" para T4.
    /// </summary>
    /// <remarks>
    /// <b>El DedupeKey lleva el Status a propósito.</b> Con la clave default
    /// (<c>travel.order.confirmed:{orderRef}</c>) un viaje que notificó 'Partial' y luego
    /// terminó 'Confirmed' quedaría suprimido para siempre — y el segundo aviso es el que
    /// el viajero más necesita. Partial y Confirmed son hechos DISTINTOS del mismo viaje,
    /// así que cada uno lleva su propia clave; el re-confirm del mismo estado sí dedupea.
    /// </remarks>
    private NotificationEvent BuildConfirmedNotification(
        CartOrder order,
        IReadOnlyList<TravelOrderItem> items) => new(
        Type: NotificationTypes.TravelOrderConfirmed,
        SubjectId: order.OrderRef,
        ToEmail: order.GuestEmail,
        ToName: order.GuestName,
        Code: order.ConfirmationCode ?? BuildConfirmationCode(order.OrderRef),
        OccurredAt: _now(),
        Amount: order.Total,
        Currency: order.Currency,
        Lines: items
            .Select(i => new NotificationLine(i.Label, 1, i.Price, i.Currency, i.Product.ToString()))
            .ToList(),
        Data: new Dictionary<string, string> { ["Estado"] = order.Status },
        ActionPath: $"/booking/viajes/{order.OrderRef}",
        DedupeKey: $"{NotificationTypes.TravelOrderConfirmed}:{order.OrderRef}:{order.Status}");

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

        // El rastro va DESPUÉS de sellar la cancelación y es best-effort: si el registro
        // falla, la cancelación ya ocurrió y desandarla sería peor. Mismo criterio que la
        // transferencia de tickets de Eventos.
        //
        // Que exista es lo que importa. Este endpoint es ANÓNIMO —el orderRef es la
        // credencial, decisión deliberada para que quien compró como invitado vuelva a su
        // reserva— y a la vez DESTRUCTIVO y con movimiento de plata. Sin rastro no había forma
        // de responder "¿quién canceló este viaje y cuándo?", que es justo la pregunta que
        // llega cuando alguien reenvía un correo de confirmación o comparte un navegador.
        // Auditarlo no rompe la compra de invitado: no pide sesión, solo deja constancia.
        await BestEffort.RunAsync(() => AuditCancellationAsync(cancelled, cancellationToken), cancellationToken);

        return new TravelCancellationResult(
            cancelled.OrderRef, cancelled.Status, cancelled.Refunded, cancelled.RefundAmount, cancelled.Currency);
    }

    /// <summary>
    /// Deja constancia de la cancelación: quién figuraba como viajero, qué se reembolsó y
    /// cuántas líneas se soltaron.
    /// </summary>
    /// <remarks>
    /// El actor es el viajero de la orden, no quien hizo la petición: el endpoint es anónimo y
    /// no hay sesión que consultar. Es una limitación honesta del modelo de credencial-por-URL
    /// y queda dicha aquí para que nadie lea este registro como una identificación del
    /// solicitante.
    /// </remarks>
    private Task AuditCancellationAsync(CartOrder cancelled, CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return Task.CompletedTask;
        }

        var detalleReembolso = cancelled.Refunded
            ? $"reembolso {cancelled.RefundAmount} {cancelled.Currency}"
            : "sin reembolso";

        return _audit.WriteAsync(
            new AuditEvent(
                Id: Guid.NewGuid().ToString("N"),
                OccurredAtUtc: _now().UtcDateTime,
                ActorEmail: cancelled.GuestEmail,
                ActorName: cancelled.GuestName,
                Action: "travel.order.cancelled",
                Resource: cancelled.OrderRef,
                Outcome: "success",
                Detail: $"Viaje cancelado por URL-credencial; {cancelled.Lines.Count} línea(s) liberada(s); {detalleReembolso}."),
            cancellationToken);
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
            CurrentStage: currentStage,
            PaymentSessionId: order.PaymentSessionId);
    }

    // ── Carga/escritura desde el store (deserialización defensiva) ──────
    private async Task<CartOrder?> LoadAsync(string? orderRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return null;
        }
        var json = await _store.ReadAsync(ResourceType, orderRef.Trim(), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<CartOrder>(json, _json); }
        catch (JsonException) { return null; }   // corrupto → como si no existiera
    }

    private async Task<List<CartOrder>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
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
        => _store.WriteAsync(ResourceType, order.OrderRef, JsonSerializer.Serialize(order, _json), cancellationToken);

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
