using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubReturnService"/> (seam <see cref="IReturnService"/>,
/// devoluciones/RMA del marketplace — journeys J2/J5 del spec tienda.md):
/// los 4 casos canónicos (ADR 0075) — empty / happy / filter / idempotent —
/// más la máquina de estados del RMA (solicitada→aprobada/rechazada→recibida→
/// reembolsada), el reembolso vía <see cref="IShopOrderService.RefundAsync"/> y
/// el rastro en <see cref="IAuditTrailWriter"/>. Compone los stubs reales del
/// motor para ejercer el flujo end-to-end comprar → confirmar → devolver.
/// </summary>
public class StubReturnServiceTests
{
    private const string LineLaptop = "tec-laptop-pro-14/tec-laptop-pro-14-16-512";
    private const string LineHeadphones = "tec-audifonos-anc/tec-audifonos-anc-negro";

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
            Returns = new StubReturnService(Orders, Audit, now);
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

    /// <summary>
    /// El ciclo completo devuelve la LÍNEA, y el pedido sigue capturado.
    /// </summary>
    /// <remarks>
    /// <para><b>Este test afirmaba lo contrario, y afirmaba un defecto</b> (#57). Decía que tras
    /// devolver una línea la sesión de pago quedaba <c>Refunded</c> — y quedaba, porque el
    /// proveedor local <b>ignoraba el monto</b> y reembolsaba la sesión entera. El pedido tiene
    /// dos líneas: devolver la laptop marcaba también los audífonos como reembolsados, y la
    /// segunda devolución se rechazaba después por un estado que nadie había querido poner.</para>
    ///
    /// <para>Un test que codifica el defecto es peor que no tenerlo: convierte el arreglo en una
    /// regresión y obliga a discutir con el rojo antes de poder mirar el código.</para>
    /// </remarks>
    [Fact]
    public async Task FullLifecycle_RefundsTheLine_AndLeavesTheRestCaptured()
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

        // Se devolvió EXACTAMENTE la línea, no el pedido.
        var outcome = await h.Payments.GetStatusAsync(order.PaymentSessionId);
        Assert.Equal(rma.RefundAmount, outcome.AmountRefunded);
        Assert.True(rma.RefundAmount < order.Amount,
            "El pedido tiene dos líneas: si devolver una fuera devolver el total, este test no probaría nada.");

        // Y el pago sigue CAPTURADO, porque queda saldo de la otra línea.
        Assert.Equal(PaymentStatus.Captured, outcome.Status);

        // Audit: 1 solicitud + 3 transiciones.
        Assert.Equal(4, h.Audit.Events.Count);
    }

    /// <summary>
    /// Dos devoluciones se ACUMULAN, y sólo la última cierra el pago.
    /// </summary>
    /// <remarks>
    /// Es el caso que el defecto hacía imposible: con la sesión marcada <c>Refunded</c> por la
    /// primera línea, la segunda se rechazaba con «solo se reembolsa lo capturado». Nadie lo veía
    /// porque ningún test devolvía dos líneas del mismo pedido.
    /// </remarks>
    [Fact]
    public async Task TwoLines_Accumulate_AndOnlyTheLastClosesThePayment()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        async Task DevolverAsync(string linea, string motivo)
        {
            var caso = await h.Returns.RequestAsync(order.OrderRef, linea, motivo);
            await h.Returns.AdvanceAsync(caso.RmaId, ShopReturnStatus.Approved);
            await h.Returns.AdvanceAsync(caso.RmaId, ShopReturnStatus.Received);
            await h.Returns.AdvanceAsync(caso.RmaId, ShopReturnStatus.Refunded);
        }

        await DevolverAsync(LineLaptop, "Llegó dañado");
        var tras1 = await h.Payments.GetStatusAsync(order.PaymentSessionId);
        Assert.Equal(PaymentStatus.Captured, tras1.Status);

        await DevolverAsync(LineHeadphones, "Ya no los quiere");
        var tras2 = await h.Payments.GetStatusAsync(order.PaymentSessionId);

        // Devuelto TODO lo cobrado → ahí sí, y no antes.
        Assert.Equal(order.Amount, tras2.AmountRefunded);
        Assert.Equal(PaymentStatus.Refunded, tras2.Status);
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

    // ── CanRequestAsync: el veredicto que la UI dejó de deducir (#34) ──────────

    [Fact] // empty: sin devoluciones previas, una línea de una orden pagada SÍ se puede
    public async Task CanRequest_PaidOrderNoClaims_ReturnsNone()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        Assert.Equal(ShopReturnBlock.None, await h.Returns.CanRequestAsync(order.OrderRef, LineLaptop));
    }

    [Fact] // happy: el veredicto y lo que RequestAsync hace de verdad COINCIDEN
    public async Task CanRequest_AgreesWithRequest_OnEveryInput()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        // Ésta es la prueba que justifica la HU entera: si el veredicto dijera que
        // sí y el POST rechazara (o al revés), tendríamos otra vez dos reglas — que
        // es exactamente lo que pasó con la copia de la UI (#33).
        foreach (var (orderRef, lineId) in new[]
                 {
                     (order.OrderRef, LineLaptop),          // se puede
                     ("ord_inexistente", LineLaptop),       // orden que no existe
                     (order.OrderRef, "linea-ajena"),       // línea que no es suya
                 })
        {
            var verdict = await h.Returns.CanRequestAsync(orderRef, lineId);
            var accepted = true;
            try
            {
                await h.Returns.RequestAsync(orderRef, lineId, "llegó dañado");
            }
            catch (ArgumentException)
            {
                accepted = false;
            }

            Assert.Equal(verdict == ShopReturnBlock.None, accepted);
        }
    }

    [Fact] // filter: cada negativa trae SU motivo, no un booleano
    public async Task CanRequest_Blocked_NamesTheReason()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();

        Assert.Equal(
            ShopReturnBlock.OrderNotFound,
            await h.Returns.CanRequestAsync("ord_inexistente", LineLaptop));
        Assert.Equal(
            ShopReturnBlock.LineNotInOrder,
            await h.Returns.CanRequestAsync(order.OrderRef, "producto-de-otro-pedido"));

        // Sin pagar: el checkout deja la orden en Pending hasta el confirm.
        var sinPagar = await h.Orders.CheckoutAsync(
            new[] { new ShopCartItem("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", 1) },
            Customer());
        Assert.Equal(
            ShopReturnBlock.OrderNotPaid,
            await h.Returns.CanRequestAsync(sinPagar.OrderRef, LineLaptop));
    }

    [Fact] // EL matiz: un reclamo vivo es «no» para el veredicto y ÉXITO para el POST
    public async Task CanRequest_AlreadyOpen_BlocksButRequestStaysIdempotent()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();
        var primero = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "llegó dañado");

        // Para OFRECER el botón: no, ya hay uno.
        Assert.Equal(
            ShopReturnBlock.AlreadyOpen,
            await h.Returns.CanRequestAsync(order.OrderRef, LineLaptop));

        // Para ESCRIBIR: sí, y devuelve el mismo — es lo que hace seguro un doble
        // clic. Las dos cosas son correctas y por eso lo compartido es la búsqueda,
        // no la conclusión.
        var repetido = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "llegó dañado");
        Assert.Equal(primero.RmaId, repetido.RmaId);
    }

    [Fact] // idempotent: preguntar no cambia nada — ni escribe, ni audita, ni lanza
    public async Task CanRequest_IsAPureQuery()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();
        var asientosAntes = h.Audit.Events.Count;

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ShopReturnBlock.None, await h.Returns.CanRequestAsync(order.OrderRef, LineLaptop));
        }
        // Ni con entradas basura: un veredicto que lanza obligaría a envolverlo en
        // try/catch en el controlador, y el historial entero caería por una línea.
        Assert.Equal(ShopReturnBlock.OrderNotFound, await h.Returns.CanRequestAsync("", LineLaptop));
        Assert.Equal(ShopReturnBlock.LineNotInOrder, await h.Returns.CanRequestAsync(order.OrderRef, ""));

        Assert.Empty(await h.Returns.GetForOrderAsync(order.OrderRef));
        Assert.Equal(asientosAntes, h.Audit.Events.Count);
    }

    [Fact] // un rechazo previo REABRE la puerta: es el único desenlace que lo hace
    public async Task CanRequest_AfterRejection_AllowsAgain()
    {
        var h = new Harness();
        var order = await h.PaidOrderAsync();
        var rma = await h.Returns.RequestAsync(order.OrderRef, LineLaptop, "llegó dañado");

        await h.Returns.AdvanceAsync(rma.RmaId, ShopReturnStatus.Rejected);

        Assert.Equal(ShopReturnBlock.None, await h.Returns.CanRequestAsync(order.OrderRef, LineLaptop));
    }
}
