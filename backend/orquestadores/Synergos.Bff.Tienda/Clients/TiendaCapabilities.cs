using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Clients;

/// <summary>Las seis capacidades que este BFF necesita, cada una tras su cliente HTTP nombrado.</summary>
/// <remarks>
/// <b>Seis contra las cuatro de Salud, y solo tres compartidas</b> —Pricing, Payments y
/// Notifications—. Es lo que hace de Tienda el segundo consumidor honesto de la máquina de sagas:
/// si el segundo orquestador hubiera sido otro con la misma forma que el primero, «la abstracción
/// sirve» no habría querido decir nada.
/// </remarks>
public sealed class TiendaCapabilities : CapabilityClients
{
    public const string Cart = "cart";
    public const string Pricing = "pricing";
    public const string Inventory = "inventory";
    public const string Orders = "orders";
    public const string Payments = "payments";
    public const string Fulfillment = "fulfillment";

    public TiendaCapabilities(IHttpClientFactory clients) : base(clients) { }

    // ── Cart ────────────────────────────────────────────────────────────────

    public Task<Result<CartDto>> GetCartAsync(string cartId, CancellationToken ct)
        => Get<CartDto>(Cart, $"v1/carts/{cartId}", ct);

    /// <summary>Cierra la canasta. Es el último paso: mientras no cierre, el comprador puede editarla.</summary>
    public Task<Result<CartDto>> CheckoutCartAsync(string cartId, IdempotencyKey key, CancellationToken ct)
        => Post<CartDto>(Cart, $"v1/carts/{cartId}/checkout", null, key, ct);

    // ── Pricing ─────────────────────────────────────────────────────────────

    public Task<Result<QuoteDto>> QuoteAsync(IReadOnlyList<CartLineDto> lines, CancellationToken ct)
        => Post<QuoteDto>(Pricing, "v1/quotes", new
        {
            lines = lines.Select(l => new { subjectKind = l.SubjectKind, subjectId = l.SubjectId, quantity = l.Quantity }),
        }, null, ct);

    /// <summary>Publica el precio de una variante (la oferta, R-9). <paramref name="validTo"/> en ahora lo retira.</summary>
    public Task<Result<PriceDto>> SetPriceAsync(
        Ref subject, Money amount, int taxRateBasisPoints, DateTimeOffset? validFrom, DateTimeOffset? validTo,
        IdempotencyKey key, CancellationToken ct)
        => Post<PriceDto>(Pricing, "v1/prices", new
        {
            subjectKind = subject.Kind,
            subjectId = subject.Id,
            amount = new { amount = amount.Amount, currency = amount.Currency },
            taxRateBasisPoints,
            validFrom,
            validTo,
        }, key, ct);

    // ── Inventory ───────────────────────────────────────────────────────────

    /// <summary>Del <see cref="Ref"/> del producto a su ítem de existencias.</summary>
    public Task<Result<StockItemDto>> FindStockAsync(Ref subject, CancellationToken ct)
        => Get<StockItemDto>(Inventory, $"v1/items?subjectKind={Uri.EscapeDataString(subject.Kind)}&subjectId={Uri.EscapeDataString(subject.Id)}", ct);

    /// <summary>Declara el pozo de existencias de una variante (la oferta, R-9).</summary>
    /// <remarks>
    /// Sólo para uno que no existe: la capacidad rechaza declarar dos veces el mismo sujeto, y cambiar las
    /// existencias de uno que ya existe es <see cref="RestockAsync"/> con la diferencia.
    /// </remarks>
    public Task<Result<StockItemDto>> DeclareStockAsync(Ref subject, int onHand, IdempotencyKey key, CancellationToken ct)
        => Post<StockItemDto>(Inventory, "v1/items", new
        {
            subjectKind = subject.Kind,
            subjectId = subject.Id,
            onHand,
        }, key, ct);

    public Task<Result<StockItemDto>> GetStockAsync(string itemId, CancellationToken ct)
        => Get<StockItemDto>(Inventory, $"v1/items/{itemId}", ct);

    /// <summary>Fija el total en mano — un recuento. No sirve para devolver existencias.</summary>
    public Task<Result<StockItemDto>> AdjustStockAsync(string itemId, int onHand, CancellationToken ct)
        => Post<StockItemDto>(Inventory, $"v1/items/{itemId}/adjust", new { onHand }, null, ct);

    /// <summary>
    /// Devuelve existencias sumando sobre lo que haya, sin leer primero.
    /// </summary>
    /// <remarks>
    /// La llave no es decoración: un relativo reintentado suma dos veces, y el motor de sagas
    /// reintenta hasta ocho. Va determinista desde la saga para que los ocho intentos sean el
    /// mismo ajuste.
    /// </remarks>
    public Task<Result<StockItemDto>> RestockAsync(string itemId, int delta, IdempotencyKey key, CancellationToken ct)
        => Post<StockItemDto>(Inventory, $"v1/items/{itemId}/adjust", new { delta }, key, ct);

    public Task<Result<StockHoldDto>> HoldStockAsync(string itemId, int quantity, Ref forWhat, IdempotencyKey key, CancellationToken ct)
        => Post<StockHoldDto>(Inventory, $"v1/items/{itemId}/holds", new
        {
            quantity,
            forKind = forWhat.Kind,
            forId = forWhat.Id,
        }, key, ct);

    public Task<Result<StockHoldDto>> ReleaseStockAsync(string holdId, CancellationToken ct)
        => Post<StockHoldDto>(Inventory, $"v1/holds/{holdId}/release", null, null, ct);

    /// <summary>Consume el apartado: la existencia sale del inventario de verdad.</summary>
    public Task<Result<StockHoldDto>> ConsumeStockAsync(string holdId, CancellationToken ct)
        => Post<StockHoldDto>(Inventory, $"v1/holds/{holdId}/consume", null, null, ct);

    // ── Orders ──────────────────────────────────────────────────────────────

    public Task<Result<OrderDto>> PlaceOrderAsync(
        Ref buyer, IReadOnlyList<(CartLineDto Line, Money UnitPrice)> lines, Money total,
        IdempotencyKey key, CancellationToken ct)
        => Post<OrderDto>(Orders, "v1/orders", new
        {
            buyerKind = buyer.Kind,
            buyerId = buyer.Id,
            lines = lines.Select(l => new
            {
                subjectKind = l.Line.SubjectKind,
                subjectId = l.Line.SubjectId,
                quantity = l.Line.Quantity,
                unitPrice = new { amount = l.UnitPrice.Amount, currency = l.UnitPrice.Currency },
            }),
            total = new { amount = total.Amount, currency = total.Currency },
        }, key, ct);

    public Task<Result<OrderDto>> FulfillOrderAsync(string orderId, CancellationToken ct)
        => Post<OrderDto>(Orders, $"v1/orders/{orderId}/fulfill", null, null, ct);

    public Task<Result<OrderDto>> CancelOrderAsync(string orderId, CancellationToken ct)
        => Post<OrderDto>(Orders, $"v1/orders/{orderId}/cancel", null, null, ct);

    public Task<Result<OrderDto>> GetOrderAsync(string orderId, CancellationToken ct)
        => Get<OrderDto>(Orders, $"v1/orders/{orderId}", ct);

    // ── Payments ────────────────────────────────────────────────────────────

    public Task<Result<PaymentDto>> AuthorizeAsync(Ref forWhat, Ref payer, Money amount, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, "v1/payments", new
        {
            forKind = forWhat.Kind,
            forId = forWhat.Id,
            payerKind = payer.Kind,
            payerId = payer.Id,
            amount = new { amount = amount.Amount, currency = amount.Currency },
        }, key, ct);

    public Task<Result<PaymentDto>> CaptureAsync(string paymentId, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/capture", null, key, ct);

    public Task<Result<PaymentDto>> VoidAsync(string paymentId, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/void", null, null, ct);

    public Task<Result<PaymentDto>> RefundAsync(string paymentId, Money amount, string reason, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/refund", new
        {
            amount = new { amount = amount.Amount, currency = amount.Currency },
            reason,
        }, key, ct);

    public Task<Result<PaymentDto>> GetPaymentAsync(string paymentId, CancellationToken ct)
        => Get<PaymentDto>(Payments, $"v1/payments/{paymentId}", ct);

    // ── Fulfillment ─────────────────────────────────────────────────────────

    public Task<Result<ShipmentDto>> ShipAsync(Ref forWhat, object address, string carrier, IdempotencyKey key, CancellationToken ct)
        => Post<ShipmentDto>(Fulfillment, "v1/shipments", new
        {
            forKind = forWhat.Kind,
            forId = forWhat.Id,
            to = address,
            carrier,
        }, key, ct);
}
