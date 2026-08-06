namespace Synergos.CMS.Interfaces;

/// <summary>
/// Ciclo de vida de una devolución/reclamo (RMA) del marketplace:
/// solicitada → aprobada/rechazada → recibida → reembolsada.
/// <see cref="Rejected"/> y <see cref="Refunded"/> son terminales.
/// </summary>
public enum ShopReturnStatus
{
    /// <summary>El comprador solicitó la devolución — esperando revisión del vendedor.</summary>
    Requested,
    /// <summary>El vendedor aprobó — esperando que el producto vuelva.</summary>
    Approved,
    /// <summary>El vendedor rechazó la solicitud (terminal).</summary>
    Rejected,
    /// <summary>El producto volvió al vendedor — listo para reembolsar.</summary>
    Received,
    /// <summary>Reembolso ejecutado vía <see cref="IPaymentProvider.RefundAsync"/> (terminal).</summary>
    Refunded,
}

/// <summary>
/// Un caso de devolución (RMA) sobre UNA línea de una orden pagada: qué se
/// devuelve, por qué, cuánto se reembolsa (el total de la línea, resuelto de
/// la orden — no del cliente) y en qué estado va.
/// </summary>
public sealed record ShopReturnCase(
    string RmaId,
    string OrderRef,
    string LineRef,
    string ProductName,
    int Quantity,
    decimal RefundAmount,
    string Currency,
    string Reason,
    ShopReturnStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt,
    string? Note);

/// <summary>
/// Devoluciones/reclamos del marketplace (dominio Tienda) — el "Necesito
/// ayuda" de Mis compras (journey J2 del spec <c>tienda.md</c>) y su
/// contraparte del vendedor (J5). Abre un RMA sobre una línea de una orden
/// pagada, lo mueve por la máquina de estados
/// (solicitada → aprobada/rechazada → recibida → reembolsada) y ejecuta el
/// reembolso vía <see cref="IPaymentProvider.RefundAsync"/> al llegar a
/// <see cref="ShopReturnStatus.Refunded"/>.
/// </summary>
/// <remarks>
/// Seam stub-first (ADR 0075): el default <c>StubReturnService</c>
/// (Application, lógica pura — ADR 0002) compone los seams existentes
/// (<see cref="IShopOrderService"/> para resolver la orden/línea real —
/// anti-tampering, <see cref="IPaymentProvider"/> para el reembolso,
/// <see cref="IAuditTrailWriter"/> para el rastro forense de cada solicitud
/// y transición — ADR 0037). Estado en memoria del proceso; un adapter real
/// delega a OMS/mediación. <see cref="RequestAsync"/> es idempotente: pedir
/// dos veces la devolución de la misma línea devuelve el MISMO caso abierto
/// (no duplica RMAs); <see cref="AdvanceAsync"/> es idempotente al
/// re-transicionar al estado actual (sin doble reembolso).
/// </remarks>
public interface IReturnService
{
    /// <summary>
    /// Abre un RMA sobre la línea <paramref name="lineId"/> (productId o
    /// "productId/variantId") de la orden <paramref name="orderRef"/>, con el
    /// motivo del comprador. El monto a reembolsar se resuelve de la línea
    /// real de la orden. Idempotente: si ya hay un caso abierto para esa
    /// línea, lo devuelve sin duplicar (solo un rechazo previo permite abrir
    /// uno nuevo). Lanza <see cref="ArgumentException"/> si la orden no
    /// existe, no está pagada, la línea no está en la orden o el motivo
    /// viene vacío.
    /// </summary>
    Task<ShopReturnCase> RequestAsync(
        string orderRef,
        string lineId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve el RMA al estado <paramref name="target"/> según la máquina
    /// legal (Requested→Approved|Rejected · Approved→Received ·
    /// Received→Refunded). Al llegar a Refunded ejecuta el reembolso del
    /// monto del caso vía <see cref="IPaymentProvider.RefundAsync"/> sobre la
    /// sesión de pago de la orden. Idempotente: re-transicionar al estado
    /// actual devuelve el caso sin cambios ni doble reembolso. Lanza
    /// <see cref="ArgumentException"/> si el RMA no existe e
    /// <see cref="InvalidOperationException"/> si la transición es ilegal o
    /// el reembolso falla.
    /// </summary>
    Task<ShopReturnCase> AdvanceAsync(
        string rmaId,
        ShopReturnStatus target,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los RMAs de una orden, del más reciente al más antiguo.
    /// Lista vacía si la orden no tiene devoluciones (o el ref viene vacío).
    /// </summary>
    Task<IReadOnlyList<ShopReturnCase>> GetForOrderAsync(
        string orderRef,
        CancellationToken cancellationToken = default);
}
