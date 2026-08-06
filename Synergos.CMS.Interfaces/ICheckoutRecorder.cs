namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam append-only para registrar checkouts completados (ADR 0097).
/// Es el punto EXPLÍCITO de captura de ventas — deliberadamente NO se
/// engancha a <c>ICartService.Clear()</c>, que es ambiguo (limpiar el
/// carrito ≠ comprar) y produciría revenue falso.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemCheckoutRecorder</c> y persiste
/// JSONL append-only en <c>App_Data/syn-dashboard/orders/{yyyy-MM-dd}.jsonl</c>
/// (un archivo por día, idempotente por <see cref="CheckoutCompleted.OrderId"/>).
///
/// Mientras el módulo de e-commerce no exponga un punto de checkout real,
/// el seam queda registrado y sin invocar: el panel de ventas muestra
/// abandono + carritos activos, sin inventar ingresos (degradación honesta).
/// </remarks>
public interface ICheckoutRecorder
{
    /// <summary>
    /// Registra un checkout completado. Idempotente por
    /// <see cref="CheckoutCompleted.OrderId"/> (no se escribe dos veces el
    /// mismo pedido el mismo día). Fire-and-forget — no lanza.
    /// </summary>
    void Record(CheckoutCompleted checkout);

    /// <summary>
    /// Devuelve los checkouts en el rango [from, to] UTC (inclusive),
    /// ordenados por <see cref="CheckoutCompleted.OccurredUtc"/> desc.
    /// Vacío si no hay datos.
    /// </summary>
    IReadOnlyList<CheckoutCompleted> GetCheckouts(DateTime fromUtc, DateTime toUtc);
}

/// <summary>
/// Snapshot inmutable de un checkout completado, persistido como JSON line.
/// </summary>
/// <param name="OrderId">Identificador único del pedido — usado para dedupe.</param>
/// <param name="LineItems">Líneas compradas (sku, cantidad, precio).</param>
/// <param name="Subtotal">Subtotal del pedido en <paramref name="Currency"/>.</param>
/// <param name="Currency">Moneda ISO 4217 (ej. <c>"COP"</c>).</param>
/// <param name="OccurredUtc">Cuándo se completó el checkout (UTC).</param>
public sealed record CheckoutCompleted(
    string OrderId,
    IReadOnlyList<CheckoutLineItem> LineItems,
    decimal Subtotal,
    string Currency,
    DateTime OccurredUtc);

/// <summary>Una línea de un checkout completado.</summary>
/// <param name="Sku">SKU del producto (o variante).</param>
/// <param name="Quantity">Cantidad comprada.</param>
/// <param name="UnitPrice">Precio unitario al momento de la compra.</param>
public sealed record CheckoutLineItem(string Sku, int Quantity, decimal UnitPrice);
