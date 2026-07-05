using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IShopOrderService"/> — motor de órdenes del marketplace
/// (dominio Tienda) server-side liviano, calcando <c>TravelCartService</c> de
/// Booking. Compone los seams existentes (<see cref="IProductCatalogProvider"/>
/// para resolver precio/stock real, <see cref="IReservationService"/> para el
/// hold de stock, <see cref="IPaymentProvider"/> para el cobro) y lleva el
/// carrito por el flujo unificado checkout → pagar (una sola vez) → confirmar.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO toca los flujos Booking/Travel (aditivo):
/// usa la vía polimórfica <see cref="IReservationService.HoldItemAsync"/> con
/// <see cref="TravelProductType.Hotel"/> como discriminador neutro (el e-commerce
/// no tiene tipo propio en el enum del motor; la identidad del producto viaja en
/// ProductRef/ProductLabel). El precio NUNCA se confía al cliente: se resuelve
/// desde el catálogo en checkout (anti-tampering). Estado (orderRef → orden +
/// reservas + sesión) en memoria (proceso), suficiente para demo; un adapter real
/// delega a DB/OMS. <see cref="ConfirmAsync"/> es idempotente: re-confirmar el
/// mismo orderRef devuelve el mismo resultado sin doble captura. ADR 0075.
/// </remarks>
public sealed class StubShopOrderService : IShopOrderService
{
    private readonly IProductCatalogProvider _catalog;
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly IOrderTrackingService? _tracking;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, OrderState> _orders = new(StringComparer.Ordinal);

    public StubShopOrderService(
        IProductCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments)
        : this(catalog, reservations, payments, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable (<paramref name="now"/>) para
    /// determinismo en tests (ADR 0002: Application sin Umbraco). Null = reloj real.
    /// </summary>
    public StubShopOrderService(
        IProductCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments,
        Func<DateTimeOffset>? now)
        : this(catalog, reservations, payments, null, now)
    {
    }

    /// <summary>
    /// Ctor completo (OLA 1 Tienda T0): <paramref name="tracking"/> opcional —
    /// si viene, la orden ALIMENTA su timeline al confirmar el pago (avanza a
    /// la etapa inicial "paid" del pipeline pago→preparación→envío→entrega;
    /// las etapas siguientes las mueve el fulfillment/vendedor vía
    /// <see cref="IOrderTrackingService.AdvanceAsync"/>). Null = sin tracking.
    /// </summary>
    public StubShopOrderService(
        IProductCatalogProvider catalog,
        IReservationService reservations,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        Func<DateTimeOffset>? now)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _tracking = tracking;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<ShopCheckoutResult> CheckoutAsync(
        IReadOnlyList<ShopCartItem> items,
        ShopCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("El carrito requiere al menos un producto.", nameof(items));
        }
        if (string.IsNullOrWhiteSpace(customer.Name) || string.IsNullOrWhiteSpace(customer.Email))
        {
            throw new ArgumentException("El nombre y el email del comprador son obligatorios.", nameof(customer));
        }

        // 1) Resolver precio/stock REAL de cada línea desde el catálogo (no se
        //    confía en el precio del cliente) + apartar el stock (un hold por
        //    línea) vía la vía polimórfica del motor de reservas.
        var lines = new List<LineState>(items.Count);
        var paymentLines = new List<PaymentLineItem>(items.Count);
        string? currency = null;
        decimal total = 0m;

        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ProductId))
            {
                throw new ArgumentException("Cada línea requiere un productId.", nameof(items));
            }
            if (item.Quantity <= 0)
            {
                throw new ArgumentException("La cantidad de cada línea debe ser mayor a cero.", nameof(items));
            }

            var detail = await _catalog.GetProductAsync(item.ProductId, cancellationToken);
            if (detail is null)
            {
                throw new ArgumentException($"Producto '{item.ProductId}' no encontrado.", nameof(items));
            }

            // Resolver precio/stock por variante (si la línea elige una) o por
            // producto base. Una variante inexistente es un error de armado.
            decimal unitPrice;
            int stock;
            string productName = detail.Product.Name;
            if (!string.IsNullOrWhiteSpace(item.VariantId))
            {
                var variant = detail.Variants.FirstOrDefault(v =>
                    string.Equals(v.VariantId, item.VariantId, StringComparison.OrdinalIgnoreCase));
                if (variant is null)
                {
                    throw new ArgumentException(
                        $"Variante '{item.VariantId}' no encontrada para el producto '{item.ProductId}'.", nameof(items));
                }
                unitPrice = variant.Price;
                stock = variant.Stock;
                productName = $"{detail.Product.Name} — {variant.Name}";
                currency ??= variant.Currency;
            }
            else
            {
                unitPrice = detail.Product.Price;
                stock = detail.Product.Stock;
                currency ??= detail.Product.Currency;
            }

            if (item.Quantity > stock)
            {
                throw new ArgumentException(
                    $"Stock insuficiente para '{productName}' (disponible {stock}, solicitado {item.Quantity}).", nameof(items));
            }

            var lineTotal = unitPrice * item.Quantity;
            var productRef = string.IsNullOrWhiteSpace(item.VariantId)
                ? item.ProductId
                : $"{item.ProductId}/{item.VariantId}";

            // Hold de stock liviano (un hold por línea) reutilizando el motor de
            // reservas. El e-commerce no tiene tipo propio en el enum del motor →
            // Hotel como discriminador neutro; la identidad real viaja en ProductRef.
            var reservation = await _reservations.HoldItemAsync(
                new TravelItemReservationRequest(
                    ProductType: TravelProductType.Hotel,
                    ProductRef: productRef,
                    ProductLabel: $"{productName} ×{item.Quantity}",
                    GuestName: customer.Name.Trim(),
                    GuestEmail: customer.Email.Trim(),
                    TotalPrice: lineTotal,
                    Currency: currency!),
                cancellationToken);

            lines.Add(new LineState(
                item.ProductId, item.VariantId, productName, item.Quantity, unitPrice, lineTotal, currency!, reservation.Id));
            paymentLines.Add(new PaymentLineItem(
                Sku: productRef,
                Description: $"{productName} ×{item.Quantity}",
                UnitPrice: unitPrice,
                Quantity: item.Quantity));
            total += lineTotal;
        }

        // 2) UNA sola sesión de pago por el total agregado del carrito.
        var orderRef = $"ord_{Guid.NewGuid():N}";
        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: orderRef,
                Amount: total,
                Currency: currency!,
                Items: paymentLines,
                CustomerEmail: customer.Email.Trim(),
                ReturnUrl: null,
                Metadata: null),
            cancellationToken);

        _orders[orderRef] = new OrderState(
            OrderRef: orderRef,
            OrderNumber: BuildOrderNumber(orderRef),
            PaymentSessionId: session.SessionId,
            CustomerName: customer.Name.Trim(),
            CustomerEmail: customer.Email.Trim(),
            Total: total,
            Currency: currency!,
            Lines: lines,
            CreatedAt: _now());

        return new ShopCheckoutResult(orderRef, session.SessionId, total, currency!);
    }

    public async Task<ShopConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef) || !_orders.TryGetValue(orderRef, out var order))
        {
            throw new ArgumentException("Orden no encontrada.", nameof(orderRef));
        }

        // Idempotente: si ya está pagada, devolver el resumen sin volver a capturar.
        if (order.Status == OrderStatus.Paid)
        {
            return ToConfirmation(order);
        }

        // 1) Capturar el pago de la orden completa (idempotente en el PSP).
        var capture = await _payments.CaptureAsync(order.PaymentSessionId, cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago de la orden (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas de stock (ConfirmAsync es idempotente
        //    por reserva). Si el hold de una línea venció, ConfirmAsync lanza
        //    InvalidOperationException → burbujea (compensación a futuro).
        foreach (var line in order.Lines)
        {
            await _reservations.ConfirmAsync(line.ReservationId, order.PaymentSessionId, cancellationToken);
        }

        var paid = order with { Status = OrderStatus.Paid };
        _orders[orderRef] = paid;

        // 3) La orden pagada alimenta su timeline de tracking (seam genérico
        //    IOrderTrackingService): etapa inicial "paid" del pipeline
        //    pago→preparación→envío→entrega. AdvanceAsync es idempotente, así
        //    que un doble confirm no duplica la etapa.
        if (_tracking is not null)
        {
            await _tracking.AdvanceAsync(
                orderRef,
                StubOrderTrackingService.StagePaid,
                $"Pago capturado — orden {paid.OrderNumber}.",
                cancellationToken);
        }

        return ToConfirmation(paid);
    }

    public Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(string customerEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            return Task.FromResult<IReadOnlyList<ShopOrder>>(Array.Empty<ShopOrder>());
        }

        var email = customerEmail.Trim();
        var orders = _orders.Values
            .Where(o => string.Equals(o.CustomerEmail, email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.CreatedAt)
            .Select(ToOrder)
            .ToList();

        return Task.FromResult<IReadOnlyList<ShopOrder>>(orders);
    }

    public Task<ShopOrder?> GetOrderAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef) || !_orders.TryGetValue(orderRef.Trim(), out var order))
        {
            return Task.FromResult<ShopOrder?>(null);
        }
        return Task.FromResult<ShopOrder?>(ToOrder(order));
    }

    private static ShopConfirmationResult ToConfirmation(OrderState order) => new(
        OrderRef: order.OrderRef,
        OrderNumber: order.OrderNumber,
        Status: order.Status.ToString(),
        Lines: order.Lines.Select(ToLine).ToList(),
        Total: order.Total,
        Currency: order.Currency);

    private static ShopOrder ToOrder(OrderState order) => new(
        OrderRef: order.OrderRef,
        OrderNumber: order.OrderNumber,
        Status: order.Status,
        CustomerName: order.CustomerName,
        CustomerEmail: order.CustomerEmail,
        Lines: order.Lines.Select(ToLine).ToList(),
        Total: order.Total,
        Currency: order.Currency,
        PaymentSessionId: order.PaymentSessionId,
        CreatedAt: order.CreatedAt);

    private static ShopOrderLine ToLine(LineState l) => new(
        ProductId: l.ProductId,
        VariantId: l.VariantId,
        ProductName: l.ProductName,
        Quantity: l.Quantity,
        UnitPrice: l.UnitPrice,
        LineTotal: l.LineTotal,
        Currency: l.Currency);

    // Número de orden human-facing derivado determinísticamente del orderRef
    // (idempotente: re-confirmar el mismo orderRef da el mismo número).
    private static string BuildOrderNumber(string orderRef)
        => "SYN-" + orderRef.Replace("ord_", string.Empty, StringComparison.Ordinal)[..8].ToUpperInvariant();

    private sealed record LineState(
        string ProductId,
        string? VariantId,
        string ProductName,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        string Currency,
        string ReservationId);

    private sealed record OrderState(
        string OrderRef,
        string OrderNumber,
        string PaymentSessionId,
        string CustomerName,
        string CustomerEmail,
        decimal Total,
        string Currency,
        IReadOnlyList<LineState> Lines,
        DateTimeOffset CreatedAt)
    {
        public OrderStatus Status { get; init; } = OrderStatus.Pending;
    }
}
