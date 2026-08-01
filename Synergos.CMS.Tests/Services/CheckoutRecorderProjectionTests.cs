using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la proyección de la venta al read-model del dashboard
/// (<see cref="ICheckoutRecorder"/>) al confirmarse el pago.
/// </summary>
/// <remarks>
/// <para>El seam estaba registrado en DI y <b>no lo invocaba nadie</b>:
/// <c>DefaultDashboardReadModel</c> leía <c>GetCheckouts()</c> y del otro lado no
/// escribía nada. Resultado: el panel de ventas mostraba <b>$0</b> por muchas
/// órdenes pagadas que hubiera en disco. Un dashboard que miente en cero es
/// peor que no tener dashboard: no se lee como "falta cablear", se lee como
/// "no vendimos".</para>
///
/// <para>El punto de enganche es <c>ConfirmAsync</c> y no el controller porque
/// es el ÚNICO paso por el que una orden llega a <c>Paid</c> — así queda cubierta
/// también la confirmación asíncrona por webhook de pago.</para>
/// </remarks>
public sealed class CheckoutRecorderProjectionTests
{
    private static ShopCustomer Customer() => new("Camila Restrepo", "camila@synergos.co");

    private static ShopCartItem Laptop(int qty = 1)
        => new("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", qty);

    private static IShopOrderService Make(ICheckoutRecorder? recorder)
        => new StubShopOrderService(
            new StubProductCatalogProvider(),
            new StubReservationService(),
            new StubPaymentProvider(),
            tracking: null,
            store: new InMemoryJsonEntityStore(),
            now: null,
            notifier: null,
            checkoutRecorder: recorder);

    [Fact]
    public async Task Confirmar_el_pago_proyecta_la_venta()
    {
        var recorder = Substitute.For<ICheckoutRecorder>();
        var svc = Make(recorder);

        var checkout = await svc.CheckoutAsync(new[] { Laptop() }, Customer());
        await svc.ConfirmAsync(checkout.OrderRef);

        recorder.Received(1).Record(Arg.Any<CheckoutCompleted>());
    }

    [Fact]
    public async Task Un_checkout_sin_confirmar_NO_cuenta_como_venta()
    {
        // Una orden Pending no es plata. Si se proyectara al crearla, el
        // dashboard inflaría ventas con carritos que nadie pagó.
        var recorder = Substitute.For<ICheckoutRecorder>();
        var svc = Make(recorder);

        await svc.CheckoutAsync(new[] { Laptop() }, Customer());

        recorder.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task La_venta_proyectada_lleva_el_total_y_la_moneda_de_la_orden()
    {
        CheckoutCompleted? captured = null;
        var recorder = Substitute.For<ICheckoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<CheckoutCompleted>()))
            .Do(call => captured = call.Arg<CheckoutCompleted>());

        var svc = Make(recorder);
        var checkout = await svc.CheckoutAsync(new[] { Laptop(qty: 2) }, Customer());
        var confirmation = await svc.ConfirmAsync(checkout.OrderRef);

        Assert.NotNull(captured);
        Assert.Equal(checkout.OrderRef, captured!.OrderId);
        Assert.Equal(confirmation.Total, captured.Subtotal);
        Assert.False(string.IsNullOrWhiteSpace(captured.Currency));
        Assert.NotEmpty(captured.LineItems);
        Assert.Equal(2, captured.LineItems[0].Quantity);
    }

    [Fact]
    public async Task El_SKU_proyectado_es_el_de_la_VARIANTE_cuando_la_hay()
    {
        // Dos tallas del mismo producto son dos SKU distintos para ventas.
        // Proyectar el productId perdería esa distinción y mezclaría en el
        // dashboard cosas que el negocio cuenta por separado.
        CheckoutCompleted? captured = null;
        var recorder = Substitute.For<ICheckoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<CheckoutCompleted>()))
            .Do(call => captured = call.Arg<CheckoutCompleted>());

        var svc = Make(recorder);
        var checkout = await svc.CheckoutAsync(new[] { Laptop() }, Customer());
        await svc.ConfirmAsync(checkout.OrderRef);

        Assert.NotNull(captured);
        Assert.Equal("tec-laptop-pro-14-16-512", captured!.LineItems[0].Sku);
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_duplica_la_venta()
    {
        // ConfirmAsync es idempotente (sale temprano si ya está Paid), así que
        // un reintento del webhook de pago no puede contar la venta dos veces.
        var recorder = Substitute.For<ICheckoutRecorder>();
        var svc = Make(recorder);

        var checkout = await svc.CheckoutAsync(new[] { Laptop() }, Customer());
        await svc.ConfirmAsync(checkout.OrderRef);
        await svc.ConfirmAsync(checkout.OrderRef);

        recorder.Received(1).Record(Arg.Any<CheckoutCompleted>());
    }

    [Fact]
    public async Task Un_recorder_que_revienta_NO_tumba_una_orden_ya_pagada()
    {
        // Best-effort, igual que el tracking y el email: la venta ya está
        // persistida cuando se proyecta. Una métrica caída no puede convertir
        // una compra buena en un error para el comprador.
        var recorder = Substitute.For<ICheckoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<CheckoutCompleted>()))
            .Do(_ => throw new InvalidOperationException("disco lleno"));

        var svc = Make(recorder);
        var checkout = await svc.CheckoutAsync(new[] { Laptop() }, Customer());

        var confirmation = await svc.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(OrderStatus.Paid.ToString(), confirmation.Status);
    }

    [Fact]
    public async Task Sin_recorder_el_motor_sigue_funcionando()
    {
        // El parámetro es opcional: los ctors anteriores y los tests que no
        // saben del dashboard no cambian.
        var svc = Make(recorder: null);

        var checkout = await svc.CheckoutAsync(new[] { Laptop() }, Customer());
        var confirmation = await svc.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(OrderStatus.Paid.ToString(), confirmation.Status);
    }
}
