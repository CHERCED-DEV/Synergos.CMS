using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la cancelación de una orden de viaje — el camino que el inventario funcional marcaba
/// como "cancelación por URL-capacidad" (ADR 0125).
/// </summary>
/// <remarks>
/// <para><b>El acceso anónimo por <c>orderRef</c> es una decisión deliberada</b> y ya está
/// fijada para la LECTURA en <c>TravelControllerAuthTests</c>: el ref es <c>trip_{Guid:N}</c>,
/// 128 bits, y tenerlo ES la autorización — es lo que permite que quien compró como invitado
/// vuelva a su reserva. Pedir sesión ahí rompería la compra de invitado sin cerrar nada.</para>
///
/// <para>Lo que NO estaba verificado era la CANCELACIÓN, que es la mitad destructiva de esa
/// misma credencial: el comentario de aquel archivo decía "y su cancelación" sin un solo test
/// detrás. Estas pruebas fijan las tres propiedades que importan cuando una acción que mueve
/// plata se abre por URL — que sea idempotente, que reembolse una sola vez, y que <b>deje
/// rastro</b>.</para>
/// </remarks>
public sealed class TravelCancellationTests
{
    private static TravelGuest Guest() => new("Ada Lovelace", "ada@synergos.test");

    // Periodo obligatorio (HU #40): el hotel de medianoche a medianoche UTC, el vuelo
    // con hora real. Cancelar no mira la ventana, pero el contrato la exige.
    private static TravelCartItem Hotel(decimal price = 600_000m)
        => new(TravelProductType.Hotel, "DLX/BB", "Habitación Deluxe (3 noches)", price, "COP",
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero));

    private static TravelCartItem Flight(decimal price = 450_000m)
        => new(TravelProductType.Flight, "SYN1010-ECOBAS", "Vuelo BOG→MDE Economy", price, "COP",
            new DateTimeOffset(2026, 9, 10, 14, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 15, 45, 0, TimeSpan.Zero));

    private sealed record Harness(
        ITravelCartService Cart,
        IPaymentProvider Payments,
        IAuditTrailWriter Audit,
        IJsonEntityStore Store);

    private static Harness Build(IAuditTrailWriter? audit = null, IJsonEntityStore? store = null)
    {
        var payments = new StubPaymentProvider();
        var theStore = store ?? new InMemoryJsonEntityStore();
        var theAudit = audit ?? Substitute.For<IAuditTrailWriter>();
        var cart = new TravelCartService(
            new StubReservationService(), payments, null, null, theStore, null, theAudit);
        return new Harness(cart, payments, theAudit, theStore);
    }

    private static async Task<string> ViajePagadoAsync(Harness h)
    {
        var checkout = await h.Cart.CheckoutAsync(new[] { Hotel(), Flight() }, Guest());
        await h.Cart.ConfirmAsync(checkout.OrderRef);
        return checkout.OrderRef;
    }

    // ── Idempotencia ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_un_viaje_pagado_lo_deja_cancelado_y_reembolsado()
    {
        var h = Build();
        var orderRef = await ViajePagadoAsync(h);

        var result = await h.Cart.CancelOrderAsync(orderRef);

        Assert.Equal("Cancelled", result.Status);
        Assert.True(result.Refunded);
        Assert.True(result.RefundAmount > 0m);
    }

    [Fact]
    public async Task Cancelar_dos_veces_devuelve_el_mismo_resultado_sellado()
    {
        // La segunda llamada no re-cancela reservas ni vuelve a pedir reembolso: devuelve lo
        // que quedó sellado. Reintentar tras un timeout de red no puede duplicar nada.
        var h = Build();
        var orderRef = await ViajePagadoAsync(h);

        var primera = await h.Cart.CancelOrderAsync(orderRef);
        var segunda = await h.Cart.CancelOrderAsync(orderRef);

        Assert.Equal(primera.Status, segunda.Status);
        Assert.Equal(primera.Refunded, segunda.Refunded);
        Assert.Equal(primera.RefundAmount, segunda.RefundAmount);
    }

    [Fact]
    public async Task Una_orden_que_no_existe_lanza_en_vez_de_cancelar_algo_ajeno()
    {
        var h = Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Cart.CancelOrderAsync("trip_deadbeefdeadbeefdeadbeefdeadbeef"));
    }

    [Fact]
    public async Task Cancelar_antes_de_pagar_no_reembolsa_nada_pero_libera_el_cupo()
    {
        // Sin captura no hay nada que devolver, y decir lo contrario sería la cifra decorativa
        // que ya costó una vez en el vertical vecino.
        var h = Build();
        var checkout = await h.Cart.CheckoutAsync(new[] { Hotel() }, Guest());

        var result = await h.Cart.CancelOrderAsync(checkout.OrderRef);

        Assert.Equal("Cancelled", result.Status);
        Assert.False(result.Refunded);
        Assert.Equal(0m, result.RefundAmount);
    }

    // ── Rastro ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_deja_rastro_de_auditoria()
    {
        var h = Build();
        var orderRef = await ViajePagadoAsync(h);

        await h.Cart.CancelOrderAsync(orderRef);

        await h.Audit.Received(1).WriteAsync(
            Arg.Is<AuditEvent>(e =>
                e.Action == "travel.order.cancelled"
                && e.Resource == orderRef
                && e.ActorEmail == "ada@synergos.test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_rastro_dice_cuantas_lineas_se_liberaron_y_cuanto_se_devolvio()
    {
        // "Se canceló" no alcanza para responder una reclamación: hace falta saber qué se
        // soltó y cuánta plata salió.
        var h = Build();
        var orderRef = await ViajePagadoAsync(h);

        await h.Cart.CancelOrderAsync(orderRef);

        await h.Audit.Received(1).WriteAsync(
            Arg.Is<AuditEvent>(e => e.Detail.Contains("2 línea") && e.Detail.Contains("reembolso")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_segunda_cancelacion_NO_vuelve_a_auditar()
    {
        // Un rastro por cancelación real. Repetirlo en cada reintento haría parecer que se
        // canceló dos veces, que es justo lo contrario de lo que la auditoría debe aclarar.
        var h = Build();
        var orderRef = await ViajePagadoAsync(h);

        await h.Cart.CancelOrderAsync(orderRef);
        await h.Cart.CancelOrderAsync(orderRef);

        await h.Audit.Received(1).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_la_auditoria_falla_la_cancelacion_igual_ocurre()
    {
        // Best-effort a propósito: la cancelación y el reembolso ya pasaron. Desandarlos
        // porque no se pudo escribir una línea de log sería el remedio peor que la enfermedad.
        var audit = Substitute.For<IAuditTrailWriter>();
        audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disco lleno"));
        var h = Build(audit);
        var orderRef = await ViajePagadoAsync(h);

        var result = await h.Cart.CancelOrderAsync(orderRef);

        Assert.Equal("Cancelled", result.Status);
        Assert.True(result.Refunded);
    }

    [Fact]
    public async Task Sin_escritor_de_auditoria_el_motor_sigue_funcionando()
    {
        // El seam es opcional en el ctor para no romper los call-sites previos. Que su
        // ausencia no cambie el comportamiento es lo que hace segura esa opcionalidad.
        var cart = new TravelCartService(new StubReservationService(), new StubPaymentProvider());
        var checkout = await cart.CheckoutAsync(new[] { Hotel() }, Guest());
        await cart.ConfirmAsync(checkout.OrderRef);

        var result = await cart.CancelOrderAsync(checkout.OrderRef);

        Assert.Equal("Cancelled", result.Status);
    }

    // ── Durabilidad ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_cancelacion_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        // El proxy de reinicio del repo. Importa aquí porque si la cancelación no
        // sobreviviera, el mismo orderRef volvería a cancelarse —y a reembolsarse— tras cada
        // reinicio.
        var store = new InMemoryJsonEntityStore();
        var payments = new StubPaymentProvider(store);

        var antes = new TravelCartService(new StubReservationService(), payments, null, null, store);
        var checkout = await antes.CheckoutAsync(new[] { Hotel() }, Guest());
        await antes.ConfirmAsync(checkout.OrderRef);
        await antes.CancelOrderAsync(checkout.OrderRef);

        var despues = new TravelCartService(new StubReservationService(), payments, null, null, store);
        var order = await despues.GetOrderAsync(checkout.OrderRef);

        Assert.NotNull(order);
        Assert.Equal("Cancelled", order!.Status);
    }
}
