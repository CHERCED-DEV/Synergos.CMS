using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la durabilidad del timeline de seguimiento y de los RMA (ADR 0116 fase 6).
/// </summary>
/// <remarks>
/// <para>Los dos vivían en un <c>ConcurrentDictionary</c> del proceso mientras
/// órdenes, pagos y reservas ya estaban en disco. Un reinicio borraba el
/// timeline de envío de órdenes que seguían "Paid", y las devoluciones que un
/// comprador ya había pedido.</para>
///
/// <para>El proxy de reinicio es el mismo que usa el repo en otros tests:
/// <b>construir un servicio nuevo sobre el MISMO store</b>. Si el estado
/// sobrevive ese cambio, sobrevive un reinicio del proceso.</para>
/// </remarks>
public sealed class TrackingAndReturnsDurabilityTests
{
    // ── Timeline de seguimiento ──────────────────────────────────────────

    [Fact]
    public async Task El_timeline_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        var before = new StubOrderTrackingService(
            StubOrderTrackingService.ShopPipeline, null, store, "tracking-shop");
        await before.AdvanceAsync("ord_1", "shipped", "Salió del centro de distribución.");

        var after = new StubOrderTrackingService(
            StubOrderTrackingService.ShopPipeline, null, store, "tracking-shop");
        var timeline = await after.GetTimelineAsync("ord_1");

        Assert.NotNull(timeline);
        Assert.Equal("shipped", timeline!.CurrentStage);
    }

    [Fact]
    public async Task Cada_dominio_tiene_su_ESPACIO_y_no_se_pisan()
    {
        // El estado guarda el índice de etapa, y los cuatro pipelines tienen
        // distinta longitud y distintos nombres. Compartir espacio haría que el
        // índice de un dominio se leyera contra el pipeline de otro: "enviado"
        // convertido en otra cosa sin que nada falle.
        var store = new InMemoryJsonEntityStore();

        var shop = new StubOrderTrackingService(
            StubOrderTrackingService.ShopPipeline, null, store, "tracking-shop");
        var academy = new StubOrderTrackingService(
            StubEnrollmentService.AcademyPipeline, null, store, "tracking-academy");

        await shop.AdvanceAsync("ref_1", "shipped");

        // La MISMA referencia en el otro dominio no existe.
        Assert.Null(await academy.GetTimelineAsync("ref_1"));
        Assert.NotNull(await shop.GetTimelineAsync("ref_1"));
    }

    [Fact]
    public async Task Un_timeline_de_OTRA_longitud_de_pipeline_se_ignora_en_vez_de_reventar()
    {
        // Si alguien cambia el pipeline de un dominio, los timelines viejos
        // quedan con otra longitud. El timeline es informativo: perderlo no
        // puede tumbar la consulta de una compra.
        var store = new InMemoryJsonEntityStore();

        var shortPipeline = new StubOrderTrackingService(
            new[] { new OrderTrackingStageDefinition("a", "A") }, null, store, "tracking-x");
        await shortPipeline.AdvanceAsync("ord_1", "a");

        var longPipeline = new StubOrderTrackingService(
            new[]
            {
                new OrderTrackingStageDefinition("a", "A"),
                new OrderTrackingStageDefinition("b", "B"),
            },
            null, store, "tracking-x");

        Assert.Null(await longPipeline.GetTimelineAsync("ord_1"));
    }

    [Fact]
    public async Task Avanzar_sigue_siendo_idempotente_y_monotonico_con_store()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = new StubOrderTrackingService(
            StubOrderTrackingService.ShopPipeline, null, store, "tracking-shop");

        await sut.AdvanceAsync("ord_1", "shipped");
        // Retroceder no retrocede.
        var back = await sut.AdvanceAsync("ord_1", "paid");

        Assert.Equal("shipped", back.CurrentStage);
    }

    // ── Devoluciones ─────────────────────────────────────────────────────

    private static async Task<(IShopOrderService orders, string orderRef)> PaidOrderAsync(
        IJsonEntityStore store)
    {
        var orders = new StubShopOrderService(
            new StubProductCatalogProvider(),
            new StubReservationService(),
            new StubPaymentProvider(store),
            tracking: null,
            store: store,
            now: null);

        var checkout = await orders.CheckoutAsync(
            new[] { new ShopCartItem("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", 1) },
            new ShopCustomer("Camila Restrepo", "camila@synergos.co"));
        await orders.ConfirmAsync(checkout.OrderRef);
        return (orders, checkout.OrderRef);
    }

    [Fact]
    public async Task Un_RMA_abierto_SOBREVIVE_a_un_servicio_nuevo()
    {
        var store = new InMemoryJsonEntityStore();
        var (orders, orderRef) = await PaidOrderAsync(store);

        var before = new StubReturnService(orders, new StubPaymentProvider(store), null, null, store);
        var rma = await before.RequestAsync(orderRef, "tec-laptop-pro-14", "No me sirvió");

        var after = new StubReturnService(orders, new StubPaymentProvider(store), null, null, store);
        var cases = await after.GetForOrderAsync(orderRef);

        Assert.Contains(cases, c => c.RmaId == rma.RmaId);
    }

    [Fact]
    public async Task La_idempotencia_por_linea_sigue_valiendo_tras_el_reinicio()
    {
        // Pedir dos veces la devolución de la misma línea no puede abrir dos
        // RMA — y eso tenía que seguir siendo cierto ahora que el estado se lee
        // del disco y no de un diccionario vivo.
        var store = new InMemoryJsonEntityStore();
        var (orders, orderRef) = await PaidOrderAsync(store);

        var before = new StubReturnService(orders, new StubPaymentProvider(store), null, null, store);
        var first = await before.RequestAsync(orderRef, "tec-laptop-pro-14", "No me sirvió");

        var after = new StubReturnService(orders, new StubPaymentProvider(store), null, null, store);
        var second = await after.RequestAsync(orderRef, "tec-laptop-pro-14", "Otra razón");

        Assert.Equal(first.RmaId, second.RmaId);
    }

    [Fact]
    public async Task El_avance_de_un_RMA_persiste()
    {
        var store = new InMemoryJsonEntityStore();
        var (orders, orderRef) = await PaidOrderAsync(store);

        var before = new StubReturnService(orders, new StubPaymentProvider(store), null, null, store);
        var rma = await before.RequestAsync(orderRef, "tec-laptop-pro-14", "No me sirvió");
        await before.AdvanceAsync(rma.RmaId, ShopReturnStatus.Approved);

        var after = new StubReturnService(orders, new StubPaymentProvider(store), null, null, store);
        var cases = await after.GetForOrderAsync(orderRef);

        Assert.Equal(ShopReturnStatus.Approved, cases.Single(c => c.RmaId == rma.RmaId).Status);
    }
}
