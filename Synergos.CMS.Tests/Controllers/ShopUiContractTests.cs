using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El contrato de <c>api/shop</c> visto <b>desde quien lo consume</b>:
/// <c>&lt;synergos-storefront&gt;</c> (repo <c>Synergos.UI</c>,
/// <c>apps/elements/modules/storefront/</c>).
/// </summary>
/// <remarks>
/// <para><b>De dónde sale la afirmación es lo que los hace distintos.</b> El resto de los
/// tests de este controller comprueban que el DTO lleve lo que el controller decidió poner;
/// acá se serializa la respuesta con las MISMAS opciones que usa ASP.NET y se comprueban las
/// claves que <c>shop-api.client.ts</c> lee, con sus mismas reglas — incluida la que más duele:
/// un normalizador que no encuentra las claves que busca <b>descarta la fila entera</b> y
/// devuelve una lista VACÍA, que no es un error y no se ve en ningún log.</para>
///
/// <para>Las claves salen de <c>shop.model.ts</c> y de los <c>normalizeX()</c> de
/// <c>shop-api.client.ts</c>. La UI es la fuente de verdad del contrato (ADR 0083).</para>
/// </remarks>
public sealed class ShopUiContractTests
{
    private const string Yo = "compradora@synergos.co";

    private sealed record Harness(
        ShopCatalogController Controller,
        IUserCollection Collections,
        IShopOrderService Orders,
        IReturnService Returns);

    private static Harness Make(bool authenticated = true)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(authenticated);
        gate.CurrentMemberEmail.Returns(authenticated ? Yo : null);
        gate.CurrentMemberKey.Returns(authenticated ? Guid.Parse("11111111-1111-1111-1111-111111111111") : (Guid?)null);

        var collections = Substitute.For<IUserCollection>();
        collections.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserCollectionItem>());

        var catalog = Substitute.For<IProductCatalogProvider>();
        catalog.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ProductSearchResult(
                new[] { new CatalogProductSummary("AUDIF-001", "Audífonos Aurora", 389_000m, "COP", "Aurora", "Audio", null, 4.6, 12, 7) },
                Array.Empty<ProductFacet>(),
                1));

        var shopQuery = Substitute.For<IShopQuery>();
        shopQuery.GetProductBySku("AUDIF-001")
            .Returns(new ProductSummary("AUDIF-001", "Audífonos Aurora", 389_000m, "COP", "/media/a.png", "/p/audif", true, "Audio"));

        var priceFormatter = Substitute.For<IPriceFormatter>();
        priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns(ci => $"$ {ci.ArgAt<decimal>(0)}");

        var orders = Substitute.For<IShopOrderService>();
        var returns = Substitute.For<IReturnService>();

        var controller = new ShopCatalogController(
            catalog,
            orders,
            priceFormatter,
            collections,
            Substitute.For<IOrderTrackingService>(),
            returns,
            Substitute.For<IMessagingService>(),
            gate,
            shopQuery,
            Substitute.For<ICatalogSocialProof>());

        return new Harness(controller, collections, orders, returns);
    }

    /// <summary>
    /// El JSON tal como sale por el cable. <see cref="JsonSerializerDefaults.Web"/> es lo que
    /// configura ASP.NET, así que las claves son las que el navegador ve.
    /// </summary>
    private static readonly JsonSerializerOptions ComoAspNet = new(JsonSerializerDefaults.Web);

    private static JsonElement Wire(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), ComoAspNet);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>La primera de las claves que traiga texto, o null. Es el <c>??</c> de la UI.</summary>
    private static string? FirstString(JsonElement value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (value.TryGetProperty(key, out var found)
                && found.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(found.GetString()))
            {
                return found.GetString();
            }
        }
        return null;
    }

    // ══════════════════════ search ══════════════════════

    /// <summary>
    /// <c>normalizeProduct</c> lee <c>title</c> ?? <c>name</c> y <c>amount</c> ?? <c>price</c>.
    /// Las canónicas son las primeras y el borde sólo emitía las segundas: funcionaba por el
    /// <c>??</c>, que es la red de seguridad y no el arreglo (ADR 0083).
    /// </summary>
    [Fact]
    public async Task Search_emite_las_claves_canonicas_del_producto()
    {
        var producto = Assert.Single(
            Wire(await Make().Controller.Search(null, null, null, null, null, null, null, default))
                .GetProperty("products").EnumerateArray());

        Assert.Equal("AUDIF-001", FirstString(producto, "id", "sku"));
        Assert.Equal("Audífonos Aurora", producto.GetProperty("title").GetString());
        Assert.Equal(389_000m, producto.GetProperty("amount").GetDecimal());
    }

    // ══════════════════════ wishlist ══════════════════════

    /// <summary>
    /// <b>La lista de favoritos salía siempre VACÍA, con el servidor lleno.</b>
    /// <c>normalizeWishlist</c> exige <c>productId</c> (?? <c>id</c>) y <c>title</c> para
    /// quedarse con una fila, y el DTO emitía <c>itemRef</c>/<c>owner</c>/<c>collection</c>:
    /// cada fila devolvía <c>null</c> y la lista filtrada quedaba en cero. Como <c>[]</c> es
    /// una respuesta válida, el cliente ni siquiera marcaba degradado — no había cartel, no
    /// había error, no había nada.
    /// </summary>
    [Fact]
    public async Task Wishlist_emite_los_favoritos_con_las_claves_que_la_UI_sabe_leer()
    {
        var h = Make();
        h.Collections.GetAsync(Yo, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserCollectionItem> { new(Yo, "wishlist", "AUDIF-001", DateTimeOffset.UtcNow) });

        var favorito = Assert.Single(
            Wire(await h.Controller.Wishlist(collection: null, CancellationToken.None))
                .GetProperty("items").EnumerateArray());

        Assert.Equal("AUDIF-001", FirstString(favorito, "productId", "id"));
        // Y con el NOMBRE del producto, resuelto con el mismo seam que pinta su tarjeta: sin
        // título la fila se descarta igual que sin id.
        Assert.Equal("Audífonos Aurora", favorito.GetProperty("title").GetString());
        Assert.Equal(389_000m, favorito.GetProperty("amount").GetDecimal());
        Assert.Equal("COP", FirstString(favorito, "currency"));
    }

    /// <summary>
    /// <b>Guardar un favorito NUNCA funcionó contra este borde.</b> La UI manda
    /// <c>{ productId, action }</c> y el record exigía <c>itemRef</c>, así que el POST
    /// contestaba <c>400</c> siempre — y el <c>catch</c> del cliente lo tapaba con una lista
    /// local optimista: el corazón se encendía y no se guardaba nada. Es la regla 9 del
    /// <c>CLAUDE.md</c> de la UI: un fallo que ocurre el 100 % de las veces se ve igual que uno
    /// que no ocurre nunca, si hay un mock detrás.
    /// </summary>
    [Fact]
    public async Task WishlistAdd_acepta_el_productId_que_manda_la_UI()
    {
        var h = Make();
        h.Collections.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserCollectionItem(Yo, "wishlist", "AUDIF-001", DateTimeOffset.UtcNow));

        var result = await h.Controller.WishlistAdd(
            new ShopCatalogController.WishlistItemRequest(Owner: null, Collection: null, ItemRef: null!, ProductId: "AUDIF-001", Action: "add"),
            CancellationToken.None);

        await h.Collections.Received(1).AddAsync(Yo, Arg.Any<string>(), "AUDIF-001", Arg.Any<CancellationToken>());
        // Y devuelve la LISTA, que es lo que la UI vuelve a leer: con un ítem suelto descartaba
        // la respuesta entera y se quedaba con su copia local.
        Assert.True(Wire(result).TryGetProperty("items", out _));
    }

    /// <summary>
    /// Quitar también llega por POST, porque es por donde la UI lo manda. Sin esto el favorito
    /// se apagaba en pantalla y seguía guardado en el servidor.
    /// </summary>
    [Fact]
    public async Task WishlistAdd_con_action_remove_QUITA_el_favorito()
    {
        var h = Make();

        await h.Controller.WishlistAdd(
            new ShopCatalogController.WishlistItemRequest(Owner: null, Collection: null, ItemRef: null!, ProductId: "AUDIF-001", Action: "remove"),
            CancellationToken.None);

        await h.Collections.Received(1).RemoveAsync(Yo, Arg.Any<string>(), "AUDIF-001", Arg.Any<CancellationToken>());
        await h.Collections.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default);
    }

    // ══════════════════════ checkout ══════════════════════

    /// <summary>
    /// <b>La dirección de entrega no salía del navegador.</b> El formulario del checkout la
    /// EXIGE (<c>checkoutValid</c> pide <c>address</c> y <c>city</c>) y la manda dentro de
    /// <c>customer</c>; <c>CustomerRequest</c> no tenía los campos, así que System.Text.Json
    /// los descartaba sin decir nada. Con <c>Tienda:Mode=Bff</c> eso no es cosmético:
    /// <c>HttpShopOrderService.ConfirmAsync</c> exige dirección y ciudad y rechaza la compra
    /// sin ellas — con el cliente degradando a un acuse inventado, o sea diciéndole al
    /// comprador «pedido confirmado» sobre una compra que no se cerró.
    /// </summary>
    [Fact]
    public async Task Checkout_no_pierde_la_direccion_que_el_comprador_escribio()
    {
        var h = Make(authenticated: false);
        h.Orders.CheckoutAsync(Arg.Any<IReadOnlyList<ShopCartItem>>(), Arg.Any<ShopCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new ShopCheckoutResult("ord_1", "psp_1", 389_000m, "COP"));

        await h.Controller.Checkout(
            new ShopCatalogController.CheckoutRequest(
                Items: new[] { new ShopCatalogController.CartItemRequest("AUDIF-001", null, 1) },
                Customer: new ShopCatalogController.CustomerRequest(
                    "Ana Restrepo", "ana@correo.co", Address: "Calle 93 #12-34", City: "Bogotá")),
            default);

        await h.Orders.Received(1).CheckoutAsync(
            Arg.Any<IReadOnlyList<ShopCartItem>>(),
            Arg.Is<ShopCustomer>(c => c.Address == "Calle 93 #12-34" && c.City == "Bogotá"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Y la recupera al confirmar: la UI manda sólo <c>{ orderRef }</c>, así que si el borde no
    /// la busca en la orden, el motor que despacha la rechaza por un campo que el comprador SÍ
    /// escribió, un paso antes.
    /// </summary>
    [Fact]
    public async Task Confirm_recupera_la_direccion_capturada_en_el_checkout()
    {
        var h = Make(authenticated: false);
        var shipTo = new ShopShippingAddress("Calle 93 #12-34", null, "Bogotá", null, null, null, "Ana Restrepo");
        h.Orders.GetOrderAsync("ord_1", Arg.Any<CancellationToken>())
            .Returns(new ShopOrder(
                "ord_1", "SYN-1", OrderStatus.Pending, "Ana Restrepo", "ana@correo.co",
                Array.Empty<ShopOrderLine>(), 389_000m, "COP", "psp_1", DateTimeOffset.UtcNow,
                OwnerMemberKey: null, ShipTo: shipTo));
        h.Orders.ConfirmAsync(Arg.Any<string>(), Arg.Any<ShopShippingAddress?>(), Arg.Any<CancellationToken>())
            .Returns(new ShopConfirmationResult("ord_1", "SYN-1", "Paid", Array.Empty<ShopOrderLine>(), 389_000m, "COP"));

        await h.Controller.Confirm(new ShopCatalogController.ConfirmRequest("ord_1"), default);

        await h.Orders.Received(1).ConfirmAsync(
            "ord_1",
            Arg.Is<ShopShippingAddress?>(a => a != null && a.Line1 == "Calle 93 #12-34" && a.City == "Bogotá"),
            Arg.Any<CancellationToken>());
    }

    // ══════════════════════ devoluciones ══════════════════════

    /// <summary>
    /// <b>Un reclamo RECHAZADO se le mostraba al comprador como «resuelto».</b> El vocabulario
    /// de la UI (<c>RETURN_STATUSES</c>) tiene los cuatro finales —<c>abierto</c>,
    /// <c>en-revision</c>, <c>resuelto</c>, <c>rechazado</c>— y el mapeo del borde colapsaba
    /// <see cref="ShopReturnStatus.Rejected"/> junto con <see cref="ShopReturnStatus.Refunded"/>
    /// en <c>resuelto</c>: a quien le negaron la devolución la pantalla le decía que su reclamo
    /// quedó atendido, y su plata no llegaba nunca.
    /// </summary>
    [Fact]
    public async Task Un_reclamo_rechazado_se_dice_rechazado_y_no_resuelto()
    {
        var h = Make();
        h.Orders.GetOrderAsync("ord_1", Arg.Any<CancellationToken>())
            .Returns(new ShopOrder(
                "ord_1", "SYN-1", OrderStatus.Paid, "Ana", "ana@correo.co",
                Array.Empty<ShopOrderLine>(), 389_000m, "COP", "psp_1", DateTimeOffset.UtcNow));
        h.Returns.GetForOrderAsync("ord_1", Arg.Any<CancellationToken>())
            .Returns(new List<ShopReturnCase>
            {
                new("rma_1", "ord_1", "AUDIF-001", "Audífonos Aurora", 1, 389_000m, "COP",
                    "damaged", ShopReturnStatus.Rejected, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null),
            });

        var reclamo = Assert.Single(
            Wire(await h.Controller.ReturnsForOrder("ord_1", default)).GetProperty("returns").EnumerateArray());

        Assert.Equal("rechazado", reclamo.GetProperty("status").GetString());
        Assert.Equal("rma_1", FirstString(reclamo, "claimId", "rmaId"));
    }
}
