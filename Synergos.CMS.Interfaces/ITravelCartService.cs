namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un ítem heterogéneo del carrito de viaje (vuelo/hotel/auto/traslado)
/// seleccionado para el checkout. Discriminado por <see cref="Product"/>;
/// <see cref="OfferId"/> es el código de la oferta elegida (FlightId/FareCode,
/// RoomType/RatePlan, CategoryCode del auto). <see cref="Label"/> es la
/// descripción human-facing del ítem (e.g. "Vuelo BOG→MDE Economy"), y
/// <see cref="Price"/> su total en <see cref="Currency"/>. El motor reserva CADA
/// ítem (un hold por ítem) y abre UNA sola sesión de pago por la suma.
/// </summary>
/// <remarks>
/// <para><b>El periodo es OBLIGATORIO</b> (HU #40). Un apartado de <c>Api.Booking</c>
/// ES una ventana sobre un recurso: sin <see cref="Start"/> y <see cref="End"/> el
/// carrito no puede apartar nada contra una capacidad que sí comprueba. Funcionaba
/// hasta ahora sólo porque el motor en proceso no comprueba nada.</para>
///
/// <para>Se eligieron obligatorios y no opcionales a conciencia: con <c>null</c> por
/// defecto un cliente viejo sigue compilando y su carrito falla más tarde y más
/// lejos, ya en modo <c>Bff</c>. Un contrato que deja pasar lo que no puede cumplir
/// es peor que uno que rompe el build.</para>
///
/// <para>Y son <see cref="DateTimeOffset"/> y no <c>DateOnly</c> porque un vuelo
/// tiene hora de salida y un auto una franja; sólo el hotel se puede modelar por
/// noches. Para el hotel se usa medianoche a medianoche UTC —la misma regla que la
/// vía hotel de <c>BookingController</c>, para no inventar una segunda—, que es lo
/// que hace que dos estadías con las mismas noches se solapen en
/// <c>Api.Booking</c>.</para>
///
/// <para><b>Lo que NO hay que hacer:</b> derivar el periodo de <see cref="OfferId"/>.
/// Es inventarse una convención sobre un identificador opaco, que es exactamente el
/// error que costó una vuelta en la HU #25.</para>
/// </remarks>
public sealed record TravelCartItem(
    TravelProductType Product,
    string OfferId,
    string Label,
    decimal Price,
    string Currency,
    DateTimeOffset Start,
    DateTimeOffset End);

/// <summary>Datos del viajero/contacto principal del carrito (un check-out por reserva).</summary>
public sealed record TravelGuest(string Name, string Email);

/// <summary>Una reserva confirmada dentro del resumen del carrito (post-confirm).</summary>
public sealed record TravelOrderItem(
    TravelProductType Product,
    string OfferId,
    string Label,
    string ReservationId,
    string Status,
    decimal Price,
    string Currency);

/// <summary>
/// Resultado de <see cref="ITravelCartService.CheckoutAsync"/>: la referencia de
/// orden del carrito (agrupa todas las reservas), la sesión de pago única por el
/// total, y el monto/moneda agregados. La UI redirige al PSP con
/// <see cref="PaymentSessionId"/> y luego llama Confirm con <see cref="OrderRef"/>.
/// </summary>
public sealed record TravelCheckoutResult(
    string OrderRef,
    string PaymentSessionId,
    decimal Amount,
    string Currency);

/// <summary>
/// Resultado de <see cref="ITravelCartService.ConfirmAsync"/>: estado agregado
/// del carrito + cada reserva confirmada + el código de confirmación del viaje.
/// <see cref="Status"/> es "Confirmed" si todas las reservas quedaron confirmadas.
/// </summary>
public sealed record TravelConfirmationResult(
    string OrderRef,
    string Status,
    string ConfirmationCode,
    IReadOnlyList<TravelOrderItem> Items);

/// <summary>
/// Una orden de viaje del historial del viajero ("Mis viajes" — OLA 2 Booking,
/// doc 21 §2.2). <see cref="Status"/> es el estado agregado del carrito:
/// "PendingPayment" (checkout hecho, sin confirmar), "Confirmed" (pago capturado
/// + reservas confirmadas), "Partial" (alguna reserva no quedó confirmada) o
/// "Cancelled" (MMB canceló). <see cref="CurrentStage"/> es la etapa del
/// timeline de viaje (<c>IOrderTrackingService</c>, pipeline paid→confirmed→
/// upcoming→completed) o null si el tracking no inició. El estado de cada ítem
/// se lee EN VIVO del motor de reservas (fuente de verdad).
/// </summary>
public sealed record TravelOrder(
    string OrderRef,
    string Status,
    string? ConfirmationCode,
    string GuestName,
    string GuestEmail,
    IReadOnlyList<TravelOrderItem> Items,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CurrentStage,
    // ADR 0116 — la sesión de pago del carrito. No es un dato secreto: ya viaja
    // en TravelCheckoutResult porque la UI la necesita para ir al PSP. Se
    // expone acá para que el receptor de webhooks pueda comprobar que el evento
    // corresponde a ESTE carrito y no confirmar un viaje con la sesión de otro.
    string? PaymentSessionId = null);

/// <summary>
/// Resultado de <see cref="ITravelCartService.CancelOrderAsync"/> (MMB v1 —
/// Manage My Booking): estado final del carrito ("Cancelled"), si hubo
/// reembolso (<see cref="Refunded"/> solo cuando el pago estaba capturado) y
/// el monto devuelto. Idempotente: re-cancelar devuelve el mismo resultado
/// sin doble reembolso.
/// </summary>
public sealed record TravelCancellationResult(
    string OrderRef,
    string Status,
    bool Refunded,
    decimal RefundAmount,
    string Currency);

/// <summary>
/// Carrito de viaje multi-producto server-side liviano — es el MOTOR
/// transaccional del dominio Booking (doc booking-app-spec). Generaliza el flujo
/// de un producto (Hoteles/Aerolíneas: search → hold → pay → confirm) al caso
/// N ítems heterogéneos: toma una lista de <see cref="TravelCartItem"/>
/// (hotel|vuelo|auto), aparta CADA ítem como una reserva
/// (<see cref="IReservationService.HoldItemAsync"/>) y abre UNA sola sesión de
/// pago (<see cref="IPaymentProvider"/>) por el total agregado.
/// <see cref="ConfirmAsync"/> captura el pago y confirma TODAS las reservas del
/// carrito, devolviendo el resumen del viaje.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que el resto del motor): el default
/// <c>TravelCartService</c> (Application, lógica pura) compone los seams
/// existentes — no toca el flujo hotel del <c>BookingController</c> (aditivo).
/// <see cref="ConfirmAsync"/> es idempotente: confirmar dos veces el mismo
/// <c>orderRef</c> deja el carrito Confirmed sin doble captura ni doble efecto.
/// Estado del carrito en memoria (proceso), suficiente para demo; un adapter
/// real delega a DB/BookingSession. ADR 0002 (Application sin Umbraco) +
/// ADR 0075 (seam con tests canónicos).
/// </remarks>
public interface ITravelCartService
{
    /// <summary>
    /// Reserva cada ítem del carrito (un hold por ítem) y abre una sola sesión de
    /// pago por el total. Devuelve la referencia de orden + el id de sesión de
    /// pago + el monto agregado. Lanza <see cref="ArgumentException"/> si el
    /// carrito está vacío o un ítem es inválido (precio ≤ 0, offerId/label vacío,
    /// monedas mezcladas).
    /// </summary>
    Task<TravelCheckoutResult> CheckoutAsync(
        IReadOnlyList<TravelCartItem> items,
        TravelGuest guest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captura el pago del carrito y confirma TODAS sus reservas, devolviendo el
    /// resumen del viaje con un código de confirmación. Idempotente: re-confirmar
    /// el mismo <paramref name="orderRef"/> devuelve el mismo resultado sin doble
    /// captura. Lanza <see cref="ArgumentException"/> si el orderRef no existe e
    /// <see cref="InvalidOperationException"/> si el hold de algún ítem venció
    /// antes de confirmar.
    /// </summary>
    Task<TravelConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve las órdenes de viaje del viajero identificado por
    /// <paramref name="travelerEmail"/> (match case-insensitive contra el email
    /// registrado en el checkout), más reciente primero. Email vacío o sin
    /// órdenes → lista vacía (no lanza).
    /// </summary>
    Task<IReadOnlyList<TravelOrder>> GetTripsAsync(string travelerEmail, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la orden de viaje por referencia, o null si no existe.</summary>
    Task<TravelOrder?> GetOrderAsync(string orderRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// MMB v1 — cancela la orden de viaje completa: cancela CADA reserva del
    /// carrito (<see cref="IReservationService.CancelAsync"/> por ítem) y
    /// reembolsa vía <c>IPaymentProvider.RefundAsync</c> SOLO si el pago estaba
    /// capturado (pre-confirm no hay nada que devolver). Idempotente:
    /// re-cancelar el mismo <paramref name="orderRef"/> devuelve el mismo
    /// resultado sin doble reembolso. Lanza <see cref="ArgumentException"/> si
    /// el orderRef no existe.
    /// </summary>
    Task<TravelCancellationResult> CancelOrderAsync(string orderRef, CancellationToken cancellationToken = default);
}
