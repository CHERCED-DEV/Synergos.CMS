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
public sealed record TravelCartItem(
    TravelProductType Product,
    string OfferId,
    string Label,
    decimal Price,
    string Currency);

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
}
