using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubReturnService"/> (seam <see cref="IReturnService"/>,
/// devoluciones/RMA del marketplace — journeys J2/J5 del spec tienda.md):
/// los 4 casos canónicos (ADR 0075) — empty / happy / filter / idempotent —
/// más la máquina de estados del RMA (solicitada→aprobada/rechazada→recibida→
/// reembolsada), el reembolso vía <see cref="IPaymentProvider.RefundAsync"/> y
/// el rastro en <see cref="IAuditTrailWriter"/>. Compone los stubs reales del
/// motor para ejercer el flujo end-to-end comprar → confirmar → devolver.
/// </summary>
public class StubReturnServiceTests
{
    private const string LineLaptop = "tec-laptop-pro-14/tec-laptop-pro-14-16-512";

    private static ShopCustomer Customer() => new("Camila Restrepo", "camila@synergos.co");

    private sealed class Harness
    {
        public IShopOrderService Orders { get; }
        public IPaymentProvider Payments { get; }
        public RecordingAudit Audit { get; } = new();
        public IReturnService Returns { get; }

        public Harness(Func<DateTimeOffset>? now = null)
        {
            Payments = new StubPaymentProvider();
            Orders = new StubShopOrderService(
                new StubProductCatalogProvider(),
                new StubReservationService(),
                Payments,
                now);
            Returns = new StubReturnService(Orders, Payments, Audit, now);
        }

        /// <summary>Compra pagada real: checkout + confirm de una laptop y unos audífonos.</summary>
        public async Task<ShopCheckoutResult> PaidOrderAsync()
        {
            var checkout = await Orders.CheckoutAsync(
                new[]
                {
                    new ShopCartItem("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", 1),
                    new ShopCartItem("tec-audifonos-anc", "tec-audifonos-anc-negro", 2),
                },
                Customer());
            await Orders.ConfirmAsync(checkout.OrderRef);
            return checkout;
        }
    }

    private sealed class RecordingAudit : IAuditTrailWriter
    {
        public List<AuditEvent> Events { get; } = new();

        public Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public IReadOnlyList<AuditEvent> GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Events;

        public IReadOnlyList<AuditEvent> GetByDateRange(DateTime fromUtc, DateTime toUtc, int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Events;

        public AuditEvent? GetById(string id) => Events.Find(e => e.Id == id);
    }

    [Fact] // empty: orden sin devoluciones (o ref vacío) → lista vacía
    public async Task GetForOrder_NoReturns_ReturnsEmpty()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        Assert.Empty(await h.Returns.GetForOrderAsync(order.OrderRef));
        Assert.Empty(await h.Returns.GetForOrderAsync(""));
    }

    [Fact] // inválido: orden inexistente, orden sin pagar, línea ajena o motivo vacío lanzan
    public async Task Request_InvalidInputs_Throw()
    {
        var h = new Harness();
        var paid = await h.PaidOrderAsync();

        // Orden que no existe.
        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Returns.RequestAsync("ord_fantasma", LineLaptop, "Llegó dañado"));

        // Orden Pending (checkout sin confirm) no admite devolución.
        var pending = await h.Orders.CheckoutAsync(
            new[] { new ShopCartItem("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", 1) }, Customer());
        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Returns.RequestAsync(pending.OrderRef, LineLaptop, "Me arrepentí"));

        // Línea que no está en la orden.
        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Returns.RequestAsync(paid.OrderRef, "sku-que-no-compro", "No era lo que pedí"));

        // Motivo vacío.
        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Returns.RequestAsync(paid.OrderRef, LineLaptop, " "));
    }

    [Fact] // happy: solicitar → RMA Requested con monto de la línea real (anti-tampering) + audit
    public async Task Request_OpensRmaWithResolvedLineAmount()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        var rma = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "Llegó con la pantalla rota");

        Assert.StartsWith("rma_", rma.RmaId, StringComparison.Ordinal);
        Assert.Equal(order.OrderRef, rma.OrderRef);
        Assert.Equal(LineLaptop, rma.LineRef);
        Assert.Equal(ShopReturnStatus.Requested, rma.Status);
        // Monto = total de la línea resuelto de la ORDEN (5.200.000 × 1), no del cliente.
        Assert.Equal(5_200_000m, rma.RefundAmount);
        Assert.Equal("COP", rma.Currency);
        Assert.Equal(1, rma.Quantity);

        // Aparece en la consulta por orden.
        var forOrder = Assert.Single(await h.Returns.GetForOrderAsync(order.OrderRef));
        Assert.Equal(rma.RmaId, forOrder.RmaId);

        // Rastro forense de la solicitud (ADR 0037).
        var evt = Assert.Single(h.Audit.Events);
        Assert.Equal("shop.return-requested", evt.Action);
        Assert.Equal("camila@synergos.co", evt.ActorEmail);
    }

    [Fact] // happy/estados: solicitada → aprobada → recibida → reembolsada ejecuta el refund en el PSP
    public async Task FullLifecycle_RefundsThroughPaymentProvider()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();
        var rma = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "Llegó dañado");

        var approved = await h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Approved, "Aprobada por el vendedor");
        Assert.Equal(ShopReturnStatus.Approved, approved.Status);

        var received = await h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Received);
        Assert.Equal(ShopReturnStatus.Received, received.Status);

        var refunded = await h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Refunded);
        Assert.Equal(ShopReturnStatus.Refunded, refunded.Status);

        // El PSP quedó Refunded sobre la sesión de pago de la orden.
        var outcome = await h.Payments.GetStatusAsync(order.PaymentSessionId);
        Assert.Equal(PaymentStatus.Refunded, outcome.Status);

        // Audit: 1 solicitud + 3 transiciones.
        Assert.Equal(4, h.Audit.Events.Count);
    }

    [Fact] // estados: rechazo es terminal; y tras un rechazo se puede abrir un caso nuevo
    public async Task Rejected_IsTerminal_AndAllowsNewRequest()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();
        var rma = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "No era el color");

        var rejected = await h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Rejected, "Fuera de plazo");
        Assert.Equal(ShopReturnStatus.Rejected, rejected.Status);

        // Terminal: no se puede seguir moviendo.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Received));

        // Un rechazo previo habilita re-solicitar (caso NUEVO).
        var again = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "Insisto: llegó dañado");
        Assert.NotEqual(rma.RmaId, again.RmaId);
        Assert.Equal(2, (await h.Returns.GetForOrderAsync(order.OrderRef)).Count);
    }

    [Fact] // estados: transiciones ilegales lanzan (no se salta la máquina)
    public async Task Advance_IllegalTransitions_Throw()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();
        var rma = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "Llegó dañado");

        // Requested no puede ir directo a Received ni a Refunded.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Received));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Refunded));
        // RMA inexistente.
        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Returns.AdvanceAsync("rma_fantasma", ShopReturnStatus.Approved));
    }

    [Fact] // filter: cada orden ve solo sus RMAs
    public async Task GetForOrder_IsScopedPerOrder()
    {
        var h = new Harness();
        var first = await h.PaidOrderAsync();
        var second = await h.PaidOrderAsync();

        await h.Returns.RequestAsync(first.OrderRef, LineLaptop, "Llegó dañado");

        Assert.Single(await h.Returns.GetForOrderAsync(first.OrderRef));
        Assert.Empty(await h.Returns.GetForOrderAsync(second.OrderRef));
    }

    [Fact] // idempotent: re-solicitar la misma línea devuelve el MISMO caso; re-transicionar no duplica reembolso
    public async Task RequestAndAdvance_AreIdempotent()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        var first = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "Llegó dañado");
        var second = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "Llegó dañado (reintento)");
        Assert.Equal(first.RmaId, second.RmaId);
        Assert.Single(await h.Returns.GetForOrderAsync(order.OrderRef));

        await h.Returns.AdvanceAsync(first.RmaId, ShopReturnStatus.Approved);
        await h.Returns.AdvanceAsync(first.RmaId, ShopReturnStatus.Received);
        var refunded1 = await h.Returns.AdvanceAsync(first.RmaId, ShopReturnStatus.Refunded);
        // Re-transicionar al estado actual → mismo caso, sin segundo refund (el PSP
        // stub rechazaría un refund sobre una sesión ya Refunded).
        var refunded2 = await h.Returns.AdvanceAsync(first.RmaId, ShopReturnStatus.Refunded);

        Assert.Equal(refunded1.UpdatedAt, refunded2.UpdatedAt);
        Assert.Equal(ShopReturnStatus.Refunded, refunded2.Status);
    }
}
