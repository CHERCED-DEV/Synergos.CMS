namespace Synergos.CMS.Interfaces;

/// <summary>
/// Ciclo de vida de una reserva del vertical Hoteles. <see cref="Held"/> tras
/// el search/select (cupo apartado mientras el huésped paga), <see cref="Confirmed"/>
/// tras capturar el pago, <see cref="Cancelled"/> si se libera, <see cref="Expired"/>
/// si el hold venció antes de confirmar (el cupo se libera automáticamente).
/// </summary>
public enum ReservationStatus
{
    /// <summary>Cupo apartado mientras el huésped completa checkout/pago.</summary>
    Held,
    /// <summary>Confirmada (pago capturado) — voucher emitido.</summary>
    Confirmed,
    /// <summary>Cancelada (liberada) — la penalidad la calcula <see cref="ICancellationPolicyEvaluator"/>.</summary>
    Cancelled,
    /// <summary>Hold vencido (now &gt; ExpiresAt sin confirmar) — el cupo quedó liberado.</summary>
    Expired,
}

/// <summary>
/// Solicitud para apartar (hold) una reserva. Identifica el producto
/// reservable (Room Type × Rate Plan) + rango + ocupación + datos del
/// huésped principal + total a cobrar (lo arma la pantalla de Select desde
/// la <see cref="RoomOffer"/> elegida).
/// </summary>
public sealed record ReservationRequest(
    string RoomTypeCode,
    string RatePlanCode,
    DateOnly CheckIn,
    DateOnly CheckOut,
    IReadOnlyList<RoomOccupancy> Rooms,
    string GuestName,
    string GuestEmail,
    decimal TotalPrice,
    string Currency);

/// <summary>
/// Tipo de producto reservable del carrito de viaje multi-producto. El motor es
/// polimórfico sobre esto: cada ítem (vuelo/hotel/auto/traslado) se aparta como
/// una reserva, todas se agrupan bajo una sola sesión de pago.
/// </summary>
public enum TravelProductType
{
    /// <summary>Habitación de hotel (Room Type × Rate Plan).</summary>
    Hotel,
    /// <summary>Vuelo (itinerario × familia tarifaria).</summary>
    Flight,
    /// <summary>Alquiler de auto (categoría × proveedor).</summary>
    Car,
    /// <summary>Traslado (aeropuerto ↔ hotel).</summary>
    Transfer,
}

/// <summary>
/// Solicitud GENÉRICA para apartar un ítem heterogéneo del carrito de viaje
/// (vuelo/hotel/auto/traslado). Generaliza <see cref="ReservationRequest"/>
/// (que es la forma hotel) a un ítem polimórfico: identifica el producto por
/// <see cref="ProductType"/> + <see cref="ProductRef"/> (offerId/código del
/// proveedor) + una etiqueta human-facing + total a cobrar. La especialización
/// (room/rate, fare, categoría de auto) viaja como <see cref="ProductRef"/>; el
/// motor de pago solo necesita total + descripción. Lo arma la sesión de
/// carrito desde la <c>TravelCartItem</c> elegida, sin acoplar el motor a la
/// forma de ningún producto. (Doc booking-app-spec §4 — <c>ITravelProductProvider</c>.)
/// </summary>
public sealed record TravelItemReservationRequest(
    TravelProductType ProductType,
    string ProductRef,
    string ProductLabel,
    string GuestName,
    string GuestEmail,
    decimal TotalPrice,
    string Currency);

/// <summary>
/// Estado de una reserva. <see cref="PaymentSessionId"/> queda poblado al
/// confirmar (liga la reserva con la sesión del <see cref="IPaymentProvider"/>).
/// <see cref="ExpiresAt"/> marca el límite del hold: si se confirma después de
/// ese instante (aún <see cref="ReservationStatus.Held"/>) la confirmación falla
/// y el auto-cancel la transiciona a <see cref="ReservationStatus.Expired"/>.
/// </summary>
public sealed record Reservation(
    string Id,
    ReservationStatus Status,
    string RoomTypeCode,
    string RatePlanCode,
    DateOnly CheckIn,
    DateOnly CheckOut,
    string GuestName,
    string GuestEmail,
    decimal TotalPrice,
    string Currency,
    string? PaymentSessionId = null,
    DateTimeOffset? ExpiresAt = null,
    TravelProductType ProductType = TravelProductType.Hotel,
    string? ProductRef = null,
    string? ProductLabel = null);

/// <summary>
/// Servicio de reservas del vertical Hoteles. Es la pieza del MOTOR que
/// mantiene el estado de la reserva entre los pasos del wizard:
/// <see cref="HoldAsync"/> (apartar) → checkout/PSP → <see cref="ConfirmAsync"/>
/// (al capturar el pago) o <see cref="CancelAsync"/> (liberar).
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IPaymentProvider"/>): el default
/// <c>StubReservationService</c> (Application, lógica pura) mantiene el estado
/// en memoria para que la demo corra end-to-end; el adapter real (PMS / DB)
/// se enchufa después sin tocar el motor. <see cref="ConfirmAsync"/> es
/// idempotente (confirmar dos veces deja la reserva Confirmed, sin doble
/// efecto). ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface IReservationService
{
    /// <summary>Aparta una reserva (estado <see cref="ReservationStatus.Held"/>).</summary>
    Task<Reservation> HoldAsync(ReservationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aparta un ítem GENÉRICO del carrito de viaje (vuelo/hotel/auto/traslado)
    /// como una reserva <see cref="ReservationStatus.Held"/>. Es la vía
    /// polimórfica que usa la sesión de carrito multi-producto
    /// (<c>ITravelCartService</c>) para apartar ítems heterogéneos bajo una sola
    /// transacción, sin acoplar el motor a la forma hotel de
    /// <see cref="HoldAsync(ReservationRequest, CancellationToken)"/> (que sigue
    /// intacta para el flujo Hoteles). El resto del ciclo de vida (Confirm /
    /// Cancel / Get / ExpireStaleHolds) es común a ambos tipos de hold.
    /// </summary>
    Task<Reservation> HoldItemAsync(TravelItemReservationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma una reserva apartada ligándola a la sesión de pago capturada.
    /// Idempotente: confirmar una reserva ya Confirmed devuelve el mismo estado.
    /// </summary>
    Task<Reservation> ConfirmAsync(string reservationId, string paymentSessionId, CancellationToken cancellationToken = default);

    /// <summary>Cancela (libera) una reserva, registrando el motivo.</summary>
    Task<Reservation> CancelAsync(string reservationId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la reserva por id, o null si no existe.</summary>
    Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transiciona a <see cref="ReservationStatus.Expired"/> todos los holds
    /// vencidos (<see cref="ReservationStatus.Held"/> con <c>now &gt; ExpiresAt</c>),
    /// liberando el cupo. Idempotente y seguro de correr en bucle: una reserva
    /// ya Confirmed/Cancelled/Expired no se toca. Lo invoca el background scanner
    /// (<c>HoldExpirationScannerHostedService</c>) cada ~1-2 min. Devuelve cuántas
    /// reservas expiró en esta pasada.
    /// </summary>
    Task<int> ExpireStaleHoldsAsync(CancellationToken cancellationToken = default);
}
