namespace Synergos.CMS.Interfaces;

/// <summary>
/// Ciclo de vida de una orden del marketplace (dominio Tienda). <see cref="Pending"/>
/// tras el checkout (stock apartado + sesión de pago abierta, esperando captura),
/// <see cref="Paid"/> tras capturar el pago, <see cref="Cancelled"/> si se libera.
/// El resto del fulfillment (Preparing/Shipped/Delivered) lo maneja el OMS real;
/// el stub-first del motor solo cubre el tramo transaccional Pending→Paid.
/// </summary>
public enum OrderStatus
{
    /// <summary>Stock apartado y sesión de pago abierta — esperando captura.</summary>
    Pending,
    /// <summary>Pago capturado — orden creada/confirmada.</summary>
    Paid,
    /// <summary>Cancelada (stock liberado) antes de capturar el pago.</summary>
    Cancelled,
}

/// <summary>
/// Una línea del carrito que entra al checkout: producto + variante + cantidad.
/// El precio/stock lo resuelve el motor desde el catálogo (no se confía en el
/// precio del cliente — anti-tampering). <see cref="VariantId"/> es opcional
/// (productos sin variantes).
/// </summary>
public sealed record ShopCartItem(
    string ProductId,
    string? VariantId,
    int Quantity);

/// <summary>Datos del comprador del checkout (un check-out por orden).</summary>
public sealed record ShopCustomer(string Name, string Email);

/// <summary>
/// Resultado de <see cref="IShopOrderService.CheckoutAsync"/>: la referencia de
/// orden (agrupa el hold de stock + la sesión de pago), la sesión de pago única
/// por el total, y el monto/moneda agregados. La UI redirige al PSP con
/// <see cref="PaymentSessionId"/> y luego llama Confirm con <see cref="OrderRef"/>.
/// </summary>
public sealed record ShopCheckoutResult(
    string OrderRef,
    string PaymentSessionId,
    decimal Amount,
    string Currency);

/// <summary>Una línea confirmada dentro del resumen de la orden (post-confirm/historial).</summary>
public sealed record ShopOrderLine(
    string ProductId,
    string? VariantId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency);

/// <summary>
/// Una orden del marketplace (confirmada o pendiente). Es el modelo persistido
/// de la compra: líneas + totales + comprador + sesión de pago + estado +
/// número de orden human-facing. Lo devuelve Confirm y el historial.
/// </summary>
public sealed record ShopOrder(
    string OrderRef,
    string OrderNumber,
    OrderStatus Status,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<ShopOrderLine> Lines,
    decimal Total,
    string Currency,
    string? PaymentSessionId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Resultado de <see cref="IShopOrderService.ConfirmAsync"/>: estado de la orden
/// tras capturar el pago + número de orden + las líneas. <see cref="Status"/> es
/// "Paid" si la captura tuvo éxito.
/// </summary>
public sealed record ShopConfirmationResult(
    string OrderRef,
    string OrderNumber,
    string Status,
    IReadOnlyList<ShopOrderLine> Lines,
    decimal Total,
    string Currency);

/// <summary>
/// Servicio de órdenes del marketplace (dominio Tienda) — es el MOTOR
/// transaccional del e-commerce, calcando <see cref="ITravelCartService"/> de
/// Booking: compone los seams existentes (<see cref="IProductCatalogProvider"/>
/// para resolver precio/stock real, <see cref="IReservationService"/> para el
/// hold de stock liviano, <see cref="IPaymentProvider"/> para el cobro) y lleva
/// el carrito por el flujo unificado checkout → pagar (una sola vez) → confirmar.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que el resto del motor): el default
/// <c>StubShopOrderService</c> (Application, lógica pura) compone los seams sin
/// tocar los flujos Booking/Travel (aditivo) — usa la vía polimórfica
/// <see cref="IReservationService.HoldItemAsync"/> para apartar stock.
/// <see cref="ConfirmAsync"/> es idempotente: confirmar dos veces el mismo
/// <c>orderRef</c> deja la orden Paid sin doble captura ni doble efecto. Estado
/// (orderRef → líneas + sesión + comprador) en memoria (proceso), suficiente
/// para demo; un adapter real delega a DB/OMS. ADR 0002 (Application sin
/// Umbraco) + ADR 0075 (seam con tests canónicos).
/// </remarks>
public interface IShopOrderService
{
    /// <summary>
    /// Resuelve precio/stock real de cada línea desde el catálogo, aparta el
    /// stock (un hold por línea) y abre UNA sola sesión de pago por el total.
    /// Devuelve la referencia de orden + el id de sesión de pago + el monto
    /// agregado. Lanza <see cref="ArgumentException"/> si el carrito está vacío,
    /// una línea es inválida (cantidad ≤ 0), un producto/variante no existe, o
    /// no hay stock suficiente.
    /// </summary>
    Task<ShopCheckoutResult> CheckoutAsync(
        IReadOnlyList<ShopCartItem> items,
        ShopCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captura el pago de la orden y la confirma (crea la orden Paid),
    /// devolviendo el resumen con número de orden + líneas. Idempotente:
    /// re-confirmar el mismo <paramref name="orderRef"/> devuelve el mismo
    /// resultado sin doble captura. Lanza <see cref="ArgumentException"/> si el
    /// orderRef no existe e <see cref="InvalidOperationException"/> si el hold de
    /// stock venció antes de confirmar.
    /// </summary>
    Task<ShopConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el historial de órdenes de un comprador (por email), de la más
    /// reciente a la más antigua. Lista vacía si no tiene órdenes.
    /// </summary>
    Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(string customerEmail, CancellationToken cancellationToken = default);
}
