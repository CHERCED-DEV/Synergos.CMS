using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubShopOrderService"/> (seam <see cref="IShopOrderService"/>,
/// motor transaccional del marketplace — dominio Tienda): los 4 casos canónicos
/// (ADR 0075) — empty / happy / filter / idempotent — más historial + expired.
/// Compone los seams reales (<see cref="StubProductCatalogProvider"/> +
/// <see cref="StubReservationService"/> + <see cref="StubPaymentProvider"/>) para
/// ejercer el flujo end-to-end checkout → pagar → confirmar.
/// </summary>
public class StubShopOrderServiceTests
{
    private static ShopCustomer Customer() => new("Camila Restrepo", "camila@synergos.co");

    private static IShopOrderService Make(
        IReservationService? reservations = null,
        IPaymentProvider? payments = null,
        Func<DateTimeOffset>? now = null)
        => new StubShopOrderService(
            new StubProductCatalogProvider(),
            reservations ?? new StubReservationService(),
            payments ?? new StubPaymentProvider(),
            now);

    // Producto con variante real del catálogo sembrado.
    private static ShopCartItem Laptop(int qty = 1)
        => new("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", qty);

    private static ShopCartItem Headphones(int qty = 1)
        => new("tec-audifonos-anc", "tec-audifonos-anc-negro", qty);

    [Fact] // empty: carrito vacío lanza
    public async Task Checkout_EmptyCart_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(Array.Empty<ShopCartItem>(), Customer()));
    }

    [Fact] // inválido: cantidad no positiva lanza
    public async Task Checkout_NonPositiveQuantity_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { Laptop(qty: 0) }, Customer()));
    }

    [Fact] // inválido: producto inexistente lanza
    public async Task Checkout_UnknownProduct_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { new ShopCartItem("no-existe", null, 1) }, Customer()));
    }

    [Fact] // inválido: variante inexistente lanza
    public async Task Checkout_UnknownVariant_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { new ShopCartItem("tec-laptop-pro-14", "no-variant", 1) }, Customer()));
    }

    [Fact] // inválido: cantidad mayor al stock lanza (anti-overselling)
    public async Task Checkout_OverStock_Throws()
    {
        // La variante 16/512 tiene 12 de stock.
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { Laptop(qty: 9999) }, Customer()));
    }

    [Fact] // happy: una línea → orderRef + sesión de pago + monto = precio × qty (resuelto del catálogo)
    public async Task Checkout_SingleLine_OpensSessionForResolvedTotal()
    {
        var result = await Make().CheckoutAsync(new[] { Laptop(qty: 2) }, Customer());

        Assert.False(string.IsNullOrWhiteSpace(result.OrderRef));
        Assert.False(string.IsNullOrWhiteSpace(result.PaymentSessionId));
        // 16/512 = 5.200.000 × 2 = 10.400.000 (precio del catálogo, no del cliente).
        Assert.Equal(10_400_000m, result.Amount);
        Assert.Equal("COP", result.Currency);
    }

    [Fact] // happy: multi-línea → total suma; confirm captura y deja Paid con número de orden
    public async Task CheckoutThenConfirm_MultiLine_YieldsPaidOrder()
    {
        var reservations = new StubReservationService();
        var svc = Make(reservations);
        var items = new[] { Laptop(qty: 1), Headphones(qty: 2) };

        var checkout = await svc.CheckoutAsync(items, Customer());
        // 5.200.000 + (680.000 × 2) = 6.560.000
        Assert.Equal(6_560_000m, checkout.Amount);

        var confirm = await svc.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(OrderStatus.Paid.ToString(), confirm.Status);
        Assert.False(string.IsNullOrWhiteSpace(confirm.OrderNumber));
        Assert.Equal(2, confirm.Lines.Count);
        Assert.Equal(6_560_000m, confirm.Total);
        // Línea con variante guarda VariantId + line total = unit × qty.
        var headphones = confirm.Lines.Single(i => i.ProductId == "tec-audifonos-anc");
        Assert.Equal("tec-audifonos-anc-negro", headphones.VariantId);
        Assert.Equal(2, headphones.Quantity);
        Assert.Equal(1_360_000m, headphones.LineTotal);
    }

    [Fact] // filter/historial: GetOrders devuelve solo las del comprador, más reciente primero
    public async Task GetOrders_ReturnsOnlyMatchingCustomer_NewestFirst()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = Make(now: () => clock);

        var first = await svc.CheckoutAsync(new[] { Laptop() }, Customer());
        await svc.ConfirmAsync(first.OrderRef);
        clock = clock.AddMinutes(5);
        var second = await svc.CheckoutAsync(new[] { Headphones() }, Customer());
        await svc.ConfirmAsync(second.OrderRef);

        // Otro comprador no debe aparecer en el historial de Camila.
        await svc.CheckoutAsync(new[] { Laptop() }, new ShopCustomer("Otro", "otro@synergos.co"));

        var orders = await svc.GetOrdersAsync("camila@synergos.co");

        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal("camila@synergos.co", o.CustomerEmail));
        // Más reciente primero (el segundo checkout).
        Assert.Equal(second.OrderRef, orders[0].OrderRef);
        Assert.Equal(first.OrderRef, orders[1].OrderRef);
    }

    [Fact] // historial empty: comprador sin órdenes (o email vacío) → lista vacía
    public async Task GetOrders_NoOrders_ReturnsEmpty()
    {
        var svc = Make();
        Assert.Empty(await svc.GetOrdersAsync("nadie@synergos.co"));
        Assert.Empty(await svc.GetOrdersAsync(""));
    }

    [Fact] // idempotent: re-confirmar el mismo orderRef da el mismo resultado (sin doble efecto)
    public async Task Confirm_Twice_IsIdempotent()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync(new[] { Laptop(), Headphones() }, Customer());

        var first = await svc.ConfirmAsync(checkout.OrderRef);
        var second = await svc.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.OrderNumber, second.OrderNumber);
        Assert.Equal(first.Total, second.Total);
        // Historial conserva una sola orden (no se duplica al re-confirmar).
        var orders = await svc.GetOrdersAsync("camila@synergos.co");
        Assert.Single(orders);
        Assert.Equal(OrderStatus.Paid, orders[0].Status);
    }

    [Fact] // inválido: confirmar un orderRef inexistente lanza
    public async Task Confirm_UnknownOrder_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Make().ConfirmAsync("ord_doesnotexist"));
    }

    [Fact] // expired: si el hold de stock vence antes de confirmar, Confirm falla
    public async Task Confirm_AfterHoldExpired_Throws()
    {
        var now = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var reservations = new StubReservationService(TimeSpan.FromMinutes(10), () => now);
        var svc = Make(reservations);

        var checkout = await svc.CheckoutAsync(new[] { Laptop() }, Customer());
        now = now.AddMinutes(20); // pasa la ventana del hold

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmAsync(checkout.OrderRef));
    }
}
