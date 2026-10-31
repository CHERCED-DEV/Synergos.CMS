using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El recorrido de una orden de la puerta por la tienda del CMS (004-tienda-compra, US 6, T037): «Mis compras» la
/// lista pagada con sus líneas, el seguimiento dice «pagado», la devolución de una línea abre un solo caso, quien
/// la compró la reseña, y la ruta directa no la confirma. Una deshecha no habilita nada de eso.
/// </summary>
/// <remarks>
/// Con las piezas de verdad (el libro, el decorador, el seguimiento y las devoluciones en proceso) y un motor que no
/// sabe nada de la puerta, como el orquestador de <c>Mode=Bff</c>: lo que se ve sale del decorador, no del motor.
/// </remarks>
public sealed class OrdenDeLaPuertaEnLaTiendaTests
{
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Beto = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private const string Compra = "pta-compra-1";
    private const string Sku = "AUDIF-DIADEMA-001";

    private static CompraDeLaPuerta DeAna(string id = Compra) => new(
        id, "COMPRA01", Ana, "Ana", "ana@ejemplo.co",
        [new LineaDeLaCompra(Sku, null, "Audífonos de diadema", 1, 320_000m), new LineaDeLaCompra("TAZA-001", null, "Taza", 2, 20_000m)],
        360_000m, "COP");

    [Fact]
    public async Task Mis_compras_la_lista_pagada_con_sus_lineas_y_el_seguimiento_dice_pagado()
    {
        var t = new Tienda(Ana);
        await t.Libro.ConfirmarAsync(DeAna());

        var lista = Assert.IsType<ShopCatalogController.OrdersResponse>(Assert.IsType<OkObjectResult>(await t.Sut.Orders(CancellationToken.None)).Value);
        var orden = Assert.Single(lista.Orders);
        Assert.Equal(Compra, orden.OrderRef);
        Assert.Equal("COMPRA01", orden.OrderNumber);
        Assert.Equal(nameof(OrderStatus.Paid), orden.Status);
        Assert.Equal(360_000m, orden.Total);
        Assert.Equal(2, orden.Items.Count);

        var seguimiento = Assert.IsType<ShopCatalogController.TrackingResponse>(
            Assert.IsType<OkObjectResult>(await t.Sut.OrderTracking(Compra, CancellationToken.None)).Value);
        Assert.Equal(StubOrderTrackingService.StagePaid, seguimiento.CurrentStage);
    }

    [Fact]
    public async Task Enviada_por_el_personal_el_seguimiento_del_comprador_trae_la_guia_como_description()
    {
        var t = new Tienda(Ana);
        await t.Libro.ConfirmarAsync(DeAna());

        // Lo que deja el acto «enviar» del personal (013-seller-operaciones, T019): la etapa con la guía como nota.
        await t.Seguimiento.AdvanceAsync(Compra, "shipped", "GUIA-123");
        var seguimiento = Assert.IsType<OkObjectResult>(await t.Sut.OrderTracking(Compra, CancellationToken.None)).Value;

        // Como lo serializa MVC, que es lo que lee el storefront (normalizeTracking lee «description»).
        var etapas = JsonSerializer.SerializeToElement(
            seguimiento, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("stages");
        var enviada = etapas.EnumerateArray().Single(e => e.GetProperty("stage").GetString() == "shipped");
        Assert.Equal("GUIA-123", enviada.GetProperty("description").GetString());
        Assert.Equal("GUIA-123", enviada.GetProperty("note").GetString());
        var pagada = etapas.EnumerateArray().Single(e => e.GetProperty("stage").GetString() == StubOrderTrackingService.StagePaid);
        // Cada etapa dice como description su propia nota, la que tenga.
        Assert.Equal(pagada.GetProperty("note").GetString(), pagada.GetProperty("description").GetString());
    }

    [Fact]
    public async Task La_devolucion_de_una_linea_abre_un_solo_caso_y_otro_miembro_no_puede()
    {
        var t = new Tienda(Ana);
        await t.Libro.ConfirmarAsync(DeAna());
        var cuerpo = new ShopCatalogController.ReturnRequestBody(Sku, "No me quedaron bien.");

        var primera = Assert.IsType<ShopCatalogController.ReturnDto>(
            Assert.IsType<OkObjectResult>(await t.Sut.RequestReturn(Compra, cuerpo, CancellationToken.None)).Value);
        var otraVez = Assert.IsType<ShopCatalogController.ReturnDto>(
            Assert.IsType<OkObjectResult>(await t.Sut.RequestReturn(Compra, cuerpo, CancellationToken.None)).Value);
        Assert.Equal(primera.RmaId, otraVez.RmaId);
        Assert.Equal(320_000m, primera.RefundAmount);
        Assert.Single(await t.Devoluciones.GetForOrderAsync(Compra));

        var intruso = new Tienda(Beto, t.Almacen);
        var ajena = Assert.IsType<StatusCodeResult>(await intruso.Sut.RequestReturn(Compra, cuerpo, CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, ajena.StatusCode);
        Assert.Single(await t.Devoluciones.GetForOrderAsync(Compra));
    }

    /// <summary>
    /// Su plata la tiene el orquestador y la operación que la devuelve es del núcleo (N-6): hasta entonces la
    /// devolución llega a «recibida» y no pasa a «reembolsada» por nadie; el motor no se entera.
    /// </summary>
    [Fact]
    public async Task Su_devolucion_queda_recibida_y_no_se_reembolsa_por_el_motor()
    {
        var t = new Tienda(Ana);
        await t.Libro.ConfirmarAsync(DeAna());
        var rma = await t.Devoluciones.RequestAsync(Compra, Sku, "No me quedaron bien.");
        await t.Devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Approved);
        await t.Devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Received);

        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Refunded));

        Assert.Equal(ShopReturnStatus.Received, Assert.Single(await t.Devoluciones.GetForOrderAsync(Compra)).Status);
        await t.Motor.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Quien_la_compro_la_resena_como_comprador_verificado()
    {
        var t = new Tienda(Ana);
        await t.Libro.ConfirmarAsync(DeAna());

        Assert.IsType<OkObjectResult>(await t.Sut.SubmitReview(Sku, new(5, "Muy buenos", "Suenan bien."), CancellationToken.None));
        await t.Resenas.Received(1).UpsertReviewAsync(Arg.Is<CustomerReview>(r => r.Sku == Sku && r.AuthorKey == Ana), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Una_deshecha_no_queda_pagada_ni_se_devuelve_ni_se_resena()
    {
        var t = new Tienda(Ana);
        await t.Libro.AnotarPendienteAsync(DeAna());
        await t.Libro.DescartarAsync(Compra, Ana);

        var lista = Assert.IsType<ShopCatalogController.OrdersResponse>(Assert.IsType<OkObjectResult>(await t.Sut.Orders(CancellationToken.None)).Value);
        Assert.Equal(nameof(OrderStatus.Cancelled), Assert.Single(lista.Orders).Status);
        Assert.IsType<BadRequestObjectResult>(await t.Sut.RequestReturn(Compra, new(Sku, "x"), CancellationToken.None));
        var resena = Assert.IsType<ObjectResult>(await t.Sut.SubmitReview(Sku, new(5, null, "x"), CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, resena.StatusCode);
        await t.Resenas.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }

    [Fact]
    public async Task La_ruta_directa_no_confirma_una_de_la_puerta_y_el_motor_no_se_entera()
    {
        var t = new Tienda(Ana);
        await t.Libro.AnotarPendienteAsync(DeAna());

        Assert.IsType<BadRequestObjectResult>(await t.Sut.Confirm(new(Compra), CancellationToken.None));
        await t.Motor.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default, default);
        Assert.Equal(OrderStatus.Pending, (await t.Libro.LeerAsync(Compra))!.Status);
    }

    /// <summary>La tienda del CMS con las piezas de verdad, vista por un miembro, sobre un almacén compartido.</summary>
    private sealed class Tienda
    {
        public Tienda(Guid miembro, IJsonEntityStore? almacen = null)
        {
            Almacen = almacen ?? new InMemoryJsonEntityStore();
            var seguimiento = new StubOrderTrackingService(null, null, Almacen, null);
            Seguimiento = seguimiento;
            Libro = new LibroDeComprasDeLaPuerta(Almacen, seguimiento);

            var puerta = new PuertaSettings();
            puerta.Flujos["tienda.compra"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "tienda.comprador" };
            var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
            monitor.CurrentValue.Returns(puerta);
            var registro = new RegistroDeComprasDeLaTienda(
                Substitute.For<IHttpClientFactory>(), Libro, monitor, Substitute.For<IProductCatalogProvider>(),
                NullLogger<RegistroDeComprasDeLaTienda>.Instance, hayDestino: false);

            Motor = Substitute.For<IShopOrderService>();
            Motor.GetOrdersByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ShopOrder>());
            var ordenes = new OrdenesConLasDeLaPuerta(Motor, Libro, registro, NullLogger<OrdenesConLasDeLaPuerta>.Instance);
            Devoluciones = new StubReturnService(ordenes, null, null, Almacen);

            var gate = Substitute.For<IMemberAccessGate>();
            gate.IsAuthenticated.Returns(true);
            gate.CurrentMemberKey.Returns(miembro);
            gate.CurrentMemberDisplayName.Returns("Ana Torres");
            var consulta = Substitute.For<IShopQuery>();
            consulta.GetProductBySku(Sku).Returns(new ProductSummary(Sku, "Audífonos de diadema", 320_000m, "COP", null, "/tienda/audifonos", true, "Tecnología"));
            Resenas = Substitute.For<ICatalogSocialProof>();

            Sut = new ShopCatalogController(
                Substitute.For<IProductCatalogProvider>(), ordenes, Substitute.For<IPriceFormatter>(), Substitute.For<IUserCollection>(),
                seguimiento, Devoluciones, Substitute.For<IMessagingService>(), gate, consulta, Resenas,
                null, Options.Create(new ListadosSettings()));
        }

        public IJsonEntityStore Almacen { get; }
        public IOrderTrackingService Seguimiento { get; }
        public LibroDeComprasDeLaPuerta Libro { get; }
        public IShopOrderService Motor { get; }
        public IReturnService Devoluciones { get; }
        public ICatalogSocialProof Resenas { get; }
        public ShopCatalogController Sut { get; }
    }
}
