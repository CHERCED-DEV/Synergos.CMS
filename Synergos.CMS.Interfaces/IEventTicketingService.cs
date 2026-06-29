namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una línea del carrito de tickets: un tier elegido + (en modo reserved) el
/// asiento + la cantidad. En modo general <see cref="Seat"/> es null y
/// <see cref="Quantity"/> es la cantidad de tickets de ese tier; en modo reserved
/// cada línea es un asiento (<see cref="Quantity"/> = 1).
/// </summary>
public sealed record EventCheckoutItem(
    string Tier,
    string? Seat,
    int Quantity);

/// <summary>Datos de un asistente (uno por ticket) para emitir su e-ticket.</summary>
public sealed record EventAttendeeInfo(
    string Name,
    string Email,
    string? DocumentId = null);

/// <summary>
/// Resultado del checkout de eventos: la orden apartada + la sesión de pago
/// abierta por el total. <see cref="OrderRef"/> es la credencial idempotente para
/// confirmar; <see cref="PaymentSessionId"/> liga la orden con el
/// <see cref="IPaymentProvider"/>.
/// </summary>
public sealed record EventCheckoutResult(
    string OrderRef,
    string PaymentSessionId,
    decimal Amount,
    string Currency);

/// <summary>
/// Un e-ticket emitido al confirmar: id único + payload QR determinista (no
/// falsificable a futuro vía firma HMAC; hoy un string estable) + a qué
/// asistente / tier / asiento corresponde. Lo escanea la cara de organizador
/// (<see cref="IEventManagementService.CheckInAsync"/>).
/// </summary>
public sealed record EventTicket(
    string Id,
    string Qr,
    string EventId,
    string AttendeeName,
    string Tier,
    string? Seat);

/// <summary>
/// Resultado de confirmar la orden: estado final + los e-tickets emitidos (uno
/// por asistente/asiento). Idempotente: re-confirmar la misma orden devuelve los
/// mismos tickets sin re-emitir ni re-cobrar.
/// </summary>
public sealed record EventConfirmationResult(
    string Status,
    IReadOnlyList<EventTicket> Tickets);

/// <summary>
/// Motor transaccional del vertical Eventos (cara de asistente). Lleva la compra
/// por el flujo unificado del motor: <see cref="CheckoutAsync"/> (apartar
/// asientos/cupos + abrir UNA sesión de pago) → <see cref="ConfirmAsync"/>
/// (capturar el pago + emitir los e-tickets con QR).
/// </summary>
/// <remarks>
/// <strong>Reusa el motor (spec §4), no reinventa:</strong> cada asiento/cupo es un
/// RECURSO RESERVABLE POLIMÓRFICO (Event×Tier×Seat), igual que habitación/asiento de
/// avión. <see cref="CheckoutAsync"/> aparta cada ítem con
/// <see cref="IReservationService.HoldItemAsync"/> (hold-timeout incluido) y abre
/// UNA sola sesión con <see cref="IPaymentProvider"/> por el total — el mismo patrón
/// que <c>TravelCartService</c>/<c>StubShopOrderService</c>. <see cref="ConfirmAsync"/>
/// captura el pago, confirma las reservas y emite los e-tickets. Idempotente por
/// <c>orderRef</c>. Lógica pura en <c>Synergos.CMS.Application</c> (ADR 0002),
/// estado en memoria del proceso (demo); el adapter real delega a DB/ticketing.
/// </remarks>
public interface IEventTicketingService
{
    /// <summary>
    /// Aparta los asientos/cupos elegidos para el evento (un hold por unidad vía
    /// <see cref="IReservationService.HoldItemAsync"/>), resuelve el precio REAL
    /// desde el catálogo (anti-tampering) y abre UNA sesión de pago por el total.
    /// Lanza <see cref="ArgumentException"/> si la solicitud es inválida (evento
    /// inexistente, sin ítems/asistentes, tier inexistente, aforo insuficiente).
    /// </summary>
    Task<EventCheckoutResult> CheckoutAsync(
        string eventId,
        IReadOnlyList<EventCheckoutItem> items,
        IReadOnlyList<EventAttendeeInfo> attendees,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captura el pago de la orden, confirma todas las reservas y emite los
    /// e-tickets con QR (uno por asistente/asiento). Idempotente: re-confirmar la
    /// misma orden devuelve los mismos tickets sin re-cobrar ni re-emitir. Lanza
    /// si la orden no existe o el pago no se pudo capturar.
    /// </summary>
    Task<EventConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default);
}
