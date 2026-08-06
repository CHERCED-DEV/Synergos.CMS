using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Cubre el alta de reseñas (T10 Ola B, ADR 0114): solo COMPRADOR VERIFICADO reseña, y la
/// identidad sale del gate y NUNCA del cuerpo.
/// </summary>
/// <remarks>
/// <b>Los asserts miran el ARGUMENTO del seam, no el status HTTP.</b> Es la lección de T2: un
/// 401/403 sale IGUAL con el hueco abierto, porque el guard puede negar mientras el cuerpo del
/// método sigue escribiendo. Aquí se comprueba que <see cref="ICatalogSocialProof"/> NO recibe
/// nada cuando no debe, y que cuando recibe, el <c>AuthorKey</c> es el del gate.
/// </remarks>
public class ShopReviewSubmissionTests
{
    private static readonly Guid Comprador = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Intruso = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Sku = "AUDIF-DIADEMA-001";

    private static ProductSummary Product() =>
        new(Sku, "Audífonos de diadema", 320_000m, "COP", null, "/tienda/audifonos", true, "Tecnología");

    private static ShopOrder Order(OrderStatus status, string productId) =>
        new("ord_1", "0001", status, "Ana", "ana@correo.co",
            new[] { new ShopOrderLine(productId, null, "Audífonos", 1, 320_000m, 320_000m, "COP") },
            320_000m, "COP", null, DateTimeOffset.UtcNow, Comprador);

    private sealed record Harness(
        ShopCatalogController Controller,
        ICatalogSocialProof SocialProof);

    private static Harness Make(
        bool authenticated = true,
        Guid? memberKey = null,
        IReadOnlyList<ShopOrder>? orders = null,
        bool productExists = true)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(authenticated);
        gate.CurrentMemberKey.Returns(authenticated ? memberKey ?? Comprador : null);
        gate.CurrentMemberDisplayName.Returns("Ana Torres");

        var orderService = Substitute.For<IShopOrderService>();
        orderService.GetOrdersByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(orders ?? Array.Empty<ShopOrder>() as IReadOnlyList<ShopOrder>));

        var shopQuery = Substitute.For<IShopQuery>();
        shopQuery.GetProductBySku(Arg.Any<string>()).Returns(productExists ? Product() : null);

        var socialProof = Substitute.For<ICatalogSocialProof>();

        var controller = new ShopCatalogController(
            Substitute.For<IProductCatalogProvider>(),
            orderService,
            Substitute.For<IPriceFormatter>(),
            Substitute.For<IUserCollection>(),
            Substitute.For<IOrderTrackingService>(),
            Substitute.For<IReturnService>(),
            Substitute.For<IMessagingService>(),
            gate,
            shopQuery,
            socialProof);

        return new Harness(controller, socialProof);
    }

    private static ShopCatalogController.SubmitReviewRequest Body(int rating = 5) =>
        new(rating, "Muy bueno", "Cumple lo que promete.");

    // ── El gate: quién NO puede ───────────────────────────────────────────────
    [Fact]
    public async Task Anonimo_no_puede_resenar_y_NADA_llega_al_seam()
    {
        var h = Make(authenticated: false);

        var result = await h.Controller.SubmitReview(Sku, Body(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        // Lo que de verdad importa: el 401 podría salir con el hueco abierto.
        await h.SocialProof.DidNotReceiveWithAnyArgs()
            .UpsertReviewAsync(default!, default);
    }

    [Fact]
    public async Task Quien_NO_compro_no_puede_resenar_y_NADA_llega_al_seam()
    {
        var h = Make(memberKey: Intruso, orders: Array.Empty<ShopOrder>());

        var result = await h.Controller.SubmitReview(Sku, Body(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        await h.SocialProof.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }

    [Fact]
    public async Task Una_orden_PENDIENTE_no_habilita_a_resenar()
    {
        // Un carrito a medio pagar no es una compra: con esto valdría reseñar sin pagar.
        var h = Make(orders: new[] { Order(OrderStatus.Pending, Sku) });

        var result = await h.Controller.SubmitReview(Sku, Body(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        await h.SocialProof.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }

    [Fact]
    public async Task Haber_comprado_OTRO_producto_no_habilita_a_resenar_este()
    {
        var h = Make(orders: new[] { Order(OrderStatus.Paid, "OTRO-SKU-999") });

        var result = await h.Controller.SubmitReview(Sku, Body(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        await h.SocialProof.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }

    // ── El gate: quién SÍ, y con qué identidad ────────────────────────────────
    [Fact]
    public async Task El_comprador_resena_y_el_AuthorKey_sale_del_GATE()
    {
        var h = Make(memberKey: Comprador, orders: new[] { Order(OrderStatus.Paid, Sku) });

        var result = await h.Controller.SubmitReview(Sku, Body(rating: 4), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await h.SocialProof.Received(1).UpsertReviewAsync(
            Arg.Is<CustomerReview>(r =>
                r.Sku == Sku
                && r.AuthorKey == Comprador          // del gate, no del body
                && r.AuthorName == "Ana Torres"      // del gate, no del body
                && r.Rating == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_SKU_se_toma_del_catalogo_y_no_de_la_ruta_cruda()
    {
        // La ruta puede venir con otra caja; lo que se persiste es el SKU canónico del
        // catálogo, o dos grafías crearían dos grupos de reseñas para el mismo producto.
        var h = Make(orders: new[] { Order(OrderStatus.Paid, Sku) });

        await h.Controller.SubmitReview("audif-diadema-001", Body(), CancellationToken.None);

        await h.SocialProof.Received(1).UpsertReviewAsync(
            Arg.Is<CustomerReview>(r => r.Sku == Sku), Arg.Any<CancellationToken>());
    }

    // ── Validación ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Un_rating_fuera_de_rango_da_400_y_no_toca_el_seam(int rating)
    {
        // El seam lanza ante esto (es un fallo de programación); un cliente merece un 400,
        // no un 500.
        var h = Make(orders: new[] { Order(OrderStatus.Paid, Sku) });

        var result = await h.Controller.SubmitReview(Sku, Body(rating), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        await h.SocialProof.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }

    [Fact]
    public async Task Una_resena_sin_texto_da_400()
    {
        var h = Make(orders: new[] { Order(OrderStatus.Paid, Sku) });

        var result = await h.Controller.SubmitReview(
            Sku, new ShopCatalogController.SubmitReviewRequest(5, "Título", "   "), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        await h.SocialProof.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }

    [Fact]
    public async Task Un_producto_inexistente_da_404_y_no_toca_el_seam()
    {
        var h = Make(productExists: false, orders: new[] { Order(OrderStatus.Paid, Sku) });

        var result = await h.Controller.SubmitReview("NO-EXISTE", Body(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        await h.SocialProof.DidNotReceiveWithAnyArgs().UpsertReviewAsync(default!, default);
    }
}
