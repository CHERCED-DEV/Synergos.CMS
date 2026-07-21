using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del marketplace (dominio Tienda). Es el equivalente e-commerce del
/// <see cref="BookingController"/>/<see cref="TravelController"/>: delega la
/// búsqueda facetada + detalle del producto (PDP) a
/// <see cref="IProductCatalogProvider"/> y el flujo transaccional
/// checkout → pagar → confirmar a <see cref="IShopOrderService"/>, formateando
/// precios es-CO con <see cref="IPriceFormatter"/>. Expone el contrato que el
/// módulo Angular <c>storefront-module</c> consume:
/// <c>GET search · GET product/{id} · POST checkout · POST confirm · GET orders</c>.
/// OLA 1 Tienda (entrega A, fase T0) agrega la cara post-venta + cuenta:
/// wishlist/listas (<see cref="IUserCollection"/>), tracking de la orden
/// (<see cref="IOrderTrackingService"/>), devoluciones/RMA
/// (<see cref="IReturnService"/>) y mensajería comprador↔vendedor
/// (<see cref="IMessagingService"/>).
/// </summary>
/// <remarks>
/// Coexiste con <c>ShopController</c> (route <c>api/shop/cart</c>, carrito cookie
/// del visitante vía <see cref="ICartService"/>) sin tocarlo: este controller
/// monta los endpoints de catálogo/órdenes bajo <c>api/shop</c> (search/product/
/// checkout/confirm/orders), que no colisionan con los del carrito. La clase se
/// llama distinto (MVC requiere nombres únicos) pero la URL respeta el contrato.
///
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive
/// en los seams (Application, sin Umbraco — ADR 0002). Los seams se cambian por
/// adapters reales (Examine/Algolia para el catálogo; Stripe/Wompi/PayU para el
/// pago; OMS/DB para las órdenes) sin tocar este controller. API pública (sin
/// auth-gate): el comprador no necesita login para buscar/comprar; el orderRef es
/// la credencial para confirm, y el email del comprador agrupa su historial.
/// </remarks>
[ApiController]
[Route("api/shop")]
public sealed class ShopCatalogController : ControllerBase
{
    private const string DefaultCollection = "wishlist";

    /// <summary>
    /// Productos por página de la PLP. El tope duro lo pone <c>CatalogSettings.MaxTake</c>
    /// en el motor: aquí solo está el default cuando el cliente no pide tamaño.
    /// </summary>
    private const int DefaultPageSize = 12;

    private readonly IProductCatalogProvider _catalog;
    private readonly IShopOrderService _orders;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IUserCollection _collections;
    private readonly IOrderTrackingService _tracking;
    private readonly IReturnService _returns;
    private readonly IMessagingService _messaging;
    private readonly IMemberAccessGate _gate;

    /// <summary>
    /// Prueba social (T10, ADR 0114). El rating se resuelve AQUÍ y no en
    /// <see cref="IShopQuery"/> porque aquel seam es SÍNCRONO: bajarlo allí obligaría a un
    /// sync-over-async, y además metería un agregado de UGC dentro de un tipo de dominio del
    /// catálogo. La acción ya es el sitio donde se compone la respuesta.
    /// </summary>
    private readonly ICatalogSocialProof _socialProof;

    /// <summary>
    /// Resolución por SKU para <c>GET products/sku/{sku}</c>. Es el MISMO seam que usa el
    /// renderer Razor <c>Elements/Shop/ProductCard.cshtml</c>: así el respaldo SSR y la
    /// tarjeta ya hidratada no pueden mostrar datos distintos del mismo producto.
    /// </summary>
    private readonly IShopQuery _shopQuery;

    public ShopCatalogController(
        IProductCatalogProvider catalog,
        IShopOrderService orders,
        IPriceFormatter priceFormatter,
        IUserCollection collections,
        IOrderTrackingService tracking,
        IReturnService returns,
        IMessagingService messaging,
        IMemberAccessGate gate,
        IShopQuery shopQuery,
        ICatalogSocialProof socialProof)
    {
        _socialProof = socialProof;
        _catalog = catalog;
        _orders = orders;
        _priceFormatter = priceFormatter;
        _collections = collections;
        _tracking = tracking;
        _returns = returns;
        _messaging = messaging;
        _gate = gate;
        _shopQuery = shopQuery;
    }

    // ── T2 (doc 25) — identidad de confianza-servidor ──────────────
    // La identidad del comprador SIEMPRE se deriva de la sesión (cookie → gate),
    // nunca del body/query. RequireMember gatea la lectura-de-lo-propio + gestión.

    /// <summary>
    /// Exige un Member autenticado. Devuelve (401, default) si es anónimo, o
    /// (null, actorKey) con la key server-trusted si hay sesión. Molde de
    /// <c>DashboardApiController</c>/<c>HealthcareApiController</c> — el guard
    /// decide 401, el endpoint decide qué hace con actorKey.
    /// </summary>
    private (IActionResult? denied, Guid actorKey) RequireMember()
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid actorKey)
        {
            return (Unauthorized(new { error = "Se requiere iniciar sesión." }), default);
        }
        return (null, actorKey);
    }

    /// <summary>
    /// Guard de ownership por-orden (defensa en profundidad). El orderRef ya es
    /// una credencial bearer inadivinable (ord_{guid:N}) — el self-service de
    /// invitado depende de ella y no se rompe. Pero si la orden TIENE dueño
    /// (la colocó un member logueado), ningún OTRO member autenticado puede
    /// tocarla; admin overridea. Cierra el acceso cruzado entre members sin
    /// gatear el flujo de invitado.
    /// </summary>
    private IActionResult? DenyIfForeignMember(ShopOrder order)
    {
        if (order.OwnerMemberKey is Guid owner
            && _gate.IsAuthenticated
            && _gate.CurrentMemberKey != owner
            && !_gate.HasAnyRole("admin"))
        {
            // StatusCode(403) directo, NO Forbid(): con auth de members Forbid()
            // redirige al login (302); un API quiere el 403 limpio (molde
            // HealthcareApiController/DashboardApiController).
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        return null;
    }

    // ── 1. Search ──────────────────────────────────────────────────
    // GET /api/shop/search?q=&category=&brand=&minRating=&sort=
    // Las facetas seleccionadas llegan como query params nombrados (brand,
    // minRating) y se reenvían al provider; la respuesta trae los productos +
    // las facetas derivadas para refinar la PLP sin recargar.
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? brand,
        [FromQuery] string? minRating,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var facets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        // category va por AQUÍ y no por el parámetro suelto de abajo: la UI la manda dentro
        // de criteria.facets (storefront.ts fija category:'' siempre), o sea CSV como las
        // demás. Dejarla fuera de AddFacet resucitaba el mismo bug de multi-valor en el eje
        // de categoría: ?category=Hogar,Deportes devolvía CERO teniendo 2 y 2.
        AddFacet(facets, "category", category);
        AddFacet(facets, "brand", brand);
        AddFacet(facets, "minRating", minRating);

        // page es 1-based en la UI (shop-api.client.ts:307 manda ?page=N solo si N>1); Skip
        // es 0-based. Una page<1 se trata como la 1: un enlace roto no merece un 400.
        var pageSize = take is > 0 ? take.Value : DefaultPageSize;
        var pageNumber = page is > 0 ? page.Value : 1;

        // La multiplicación se hace en long y se capa: (page-1)*pageSize con un ?page= grande
        // DESBORDA int y sale NEGATIVO, y un Skip negativo el motor lo trata como 0 — o sea
        // que ?page=1431655766 devolvería la PÁGINA 1 como si nada, en vez de vacío.
        var skip = (int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue);

        var result = await _catalog.SearchAsync(
            new ProductQuery(
                Text: q,
                Facets: facets.Count > 0 ? facets : null,
                Sort: sort,
                Skip: skip,
                Take: pageSize),
            cancellationToken);

        var products = result.Products.Select(ToProductDto).ToList();
        // La UI (shop-api.client.ts:411 normalizeFacets) lee entry['key']; el backend
        // reshapea a esa clave (UI = fuente de verdad). Antes emitía 'field' → las
        // facetas se descartaban silenciosamente y el PLP quedaba sin filtros.
        var facetDtos = result.Facets.Select(f => new FacetDto(
            Key: f.Field,
            Label: f.Label,
            Kind: f.Kind,
            // La UI ya leía v['label'] (normalizeFacets:430) y caía al valor crudo cuando
            // faltaba — por eso el chip de rating se pintaba "4.0" en vez de "4 estrellas o
            // más". El label existía en el motor y se perdía aquí.
            Values: f.Values.Select(v => new FacetValueDto(v.Value, v.Count, v.Label ?? v.Value)).ToList())).ToList();

        return Ok(new SearchResponse(Products: products, Facets: facetDtos, Total: result.Total));
    }

    /// <summary>
    /// Añade una faceta partiendo el CSV del wire. <b>La UI manda los multi-valor como
    /// <c>?brand=Aurora,Barista</c></b> (<c>shop-api.client.ts:311</c>,
    /// <c>values.join(',')</c>), así que ésta es la convención, no una invención.
    /// </summary>
    /// <remarks>
    /// Antes el valor entraba entero como UN string y se comparaba por igualdad exacta
    /// contra la marca, así que "Aurora,Barista" no casaba con nada y el filtro devolvía
    /// CERO — filtrar por dos marcas daba menos que por una, sin error.
    /// </remarks>
    private static void AddFacet(IDictionary<string, IReadOnlyList<string>> facets, string field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }
        var values = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (values.Count > 0)
        {
            facets[field] = values;
        }
    }

    // ── 2. Product detail (PDP) ────────────────────────────────────
    // GET /api/shop/product/{id} → { product, variants, reviews, questions }
    [HttpGet("product/{id}")]
    public async Task<IActionResult> Product(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del producto es requerido." });
        }

        var detail = await _catalog.GetProductAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Producto '{id}' no encontrado." });
        }

        var product = ToProductDto(detail.Product) with { Description = detail.Description, ImageUrls = detail.ImageUrls, Images = detail.ImageUrls ?? System.Array.Empty<string>() };
        var variants = detail.Variants.Select(v => new VariantDto(
            VariantId: v.VariantId,
            Name: v.Name,
            // La UI (storefront) lee variant.label para el chip de variantes y la
            // etiqueta del carrito; sin esto mostraba el slug crudo del variantId.
            // Label = Name legible ('16 GB RAM · 512 GB SSD'). Name queda por compat.
            Label: v.Name,
            Price: v.Price,
            PriceFormatted: _priceFormatter.Format(v.Price, v.Currency),
            Currency: v.Currency,
            Stock: v.Stock,
            InStock: v.Stock > 0)).ToList();
        var reviews = detail.Reviews.Select(r => new ReviewDto(
            Author: r.Author,
            Rating: r.Rating,
            Title: r.Title,
            Body: r.Body,
            Date: r.Date)).ToList();
        var questions = detail.Questions.Select(qa => new QuestionDto(
            Asker: qa.Asker,
            Author: qa.Asker,   // clave que lee la UI en la Q&A de la PDP
            Question: qa.Question,
            Answer: qa.Answer,
            Answered: qa.Answer is not null,
            Date: qa.Date)).ToList();

        return Ok(new ProductDetailResponse(
            Product: product,
            Variants: variants,
            Reviews: reviews,
            Questions: questions));
    }

    // ── 2b. Product by SKU (tarjeta de producto) ───────────────────
    // GET /api/shop/products/sku/{sku} → Product (plano, NO envuelto)
    //
    // Existe porque el elemento Angular `product-card` pide EXACTAMENTE esta ruta
    // (product-card.ts: `/api/shop/products/sku/${sku}`) y no existía: cada tarjeta
    // montada disparaba un 404. No se reusa `product/{id}` porque aquel devuelve el
    // sobre `{product, variants, reviews, questions}` de la PDP y la tarjeta espera
    // un Product plano.
    //
    // Forma: la del contrato `Product` de `@synergos/contracts`, que es lo que la UI
    // lee (ADR 0083 — la UI es la fuente de verdad). Dos diferencias deliberadas
    // frente a `ProductDto`, y por eso NO se reusa aquel:
    //   · `sku` — `ProductDto` no lo tiene, y la tarjeta lo emite en el evento
    //     `sg:product:addToCart`.
    //   · `images` como OBJETOS `{src, alt}` — `ProductDto` los emite como `string[]`,
    //     y la tarjeta lee `images[0].src`/`images[0].alt`; con cadenas, `.src` es
    //     undefined y se pierde la imagen. No se toca `ProductDto` para no romper a
    //     search/PDP, que ya consumen el array de cadenas.
    [HttpGet("products/sku/{sku}")]
    public async Task<IActionResult> ProductBySku(string sku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return BadRequest(new { error = "El SKU del producto es requerido." });
        }

        var product = _shopQuery.GetProductBySku(sku);
        if (product is null)
        {
            return NotFound(new { error = $"Producto con SKU '{sku}' no encontrado." });
        }

        // El catálogo identifica por SKU (los ids de `search` ya son códigos tipo
        // "AUDIF-DIADEMA-001"), y `ProductSummary` no expone otro identificador, así que
        // `id` y `sku` coinciden. Se emiten los dos porque la tarjeta lee ambos.
        // T10 (ADR 0114): null cuando el producto no tiene reseñas. Se emite TAL CUAL —
        // `rating` ausente en el JSON — porque el contrato de la UI lo declara opcional
        // (`Product.rating?`) y la tarjeta degrada por AUSENCIA. Emitir un objeto con ceros
        // pintaría "0,0" en todo el catálogo (ADR 0112).
        var proof = await _socialProof.GetAsync(product.Sku, cancellationToken).ConfigureAwait(false);

        return Ok(new ProductBySkuDto(
            Id: product.Sku,
            Sku: product.Sku,
            Name: product.Name,
            Price: product.Price,
            PriceFormatted: _priceFormatter.Format(product.Price, product.Currency),
            Currency: product.Currency,
            InStock: product.InStock,
            Url: product.Url,
            Category: product.CategoryName,
            Images: product.ImageUrl is null
                ? System.Array.Empty<ProductImageDto>()
                : new[] { new ProductImageDto(Src: product.ImageUrl, Alt: product.Name) },
            Rating: proof is null ? null : new ProductRatingDto(proof.Average, proof.Count)));
    }

    // ── 3. Checkout ────────────────────────────────────────────────
    // POST /api/shop/checkout { items:[{productId,variantId,qty}], customer:{name,email} }
    //   → { orderRef, paymentSessionId, amount, currency }
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Cuerpo de la solicitud requerido." });
        }
        if (request.Customer is null
            || string.IsNullOrWhiteSpace(request.Customer.Name)
            || string.IsNullOrWhiteSpace(request.Customer.Email))
        {
            return BadRequest(new { error = "El comprador (name + email) es requerido." });
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { error = "El carrito requiere al menos un producto." });
        }

        var items = request.Items
            .Select(i => new ShopCartItem(i.ProductId, i.VariantId, i.Qty))
            .ToList();

        // T2: con sesión, la identidad es SERVER-TRUSTED (gate → cookie): la orden
        // se liga al memberKey y se ignora el name/email del body (anti-tampering —
        // no se puede colocar una orden a nombre de otro). Sin sesión → invitado con
        // los datos del form, OwnerMemberKey null (guest checkout sigue abierto).
        var customer = _gate.IsAuthenticated && _gate.CurrentMemberKey is Guid memberKey
            ? new ShopCustomer(
                Name: _gate.CurrentMemberDisplayName ?? request.Customer.Name.Trim(),
                Email: _gate.CurrentMemberEmail ?? request.Customer.Email.Trim(),
                MemberKey: memberKey)
            : new ShopCustomer(request.Customer.Name.Trim(), request.Customer.Email.Trim());

        ShopCheckoutResult result;
        try
        {
            result = await _orders.CheckoutAsync(items, customer, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new CheckoutResponse(
            OrderRef: result.OrderRef,
            PaymentSessionId: result.PaymentSessionId,
            Amount: result.Amount,
            AmountFormatted: _priceFormatter.Format(result.Amount, result.Currency),
            Currency: result.Currency));
    }

    // ── 4. Confirm ─────────────────────────────────────────────────
    // POST /api/shop/confirm { orderRef } → { status, orderNumber, items }
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderRef))
        {
            return BadRequest(new { error = "orderRef es requerido." });
        }

        ShopConfirmationResult result;
        try
        {
            result = await _orders.ConfirmAsync(request.OrderRef.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Pago no capturable / hold de stock vencido — el cliente reintenta.
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ConfirmResponse(
            Status: result.Status,
            OrderNumber: result.OrderNumber,
            OrderRef: result.OrderRef,
            Total: result.Total,
            TotalFormatted: _priceFormatter.Format(result.Total, result.Currency),
            Currency: result.Currency,
            Items: result.Lines.Select(ToOrderLineDto).ToList()));
    }

    // ── 5. Orders (historial) ──────────────────────────────────────
    // GET /api/shop/orders → { orders:[...] } — SOLO las del member logueado.
    // T2 (doc 25): guard-first. Antes tomaba ?customer=<email> y devolvía las
    // órdenes de CUALQUIER email (IDOR: email enumerable → historial ajeno). Ahora
    // la identidad viene de la sesión (server-trusted) y el filtro es por memberKey;
    // el ?customer= se ignora. Anónimo → 401.
    [HttpGet("orders")]
    public async Task<IActionResult> Orders(CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null)
        {
            return denied;
        }

        var orders = await _orders.GetOrdersByMemberAsync(actorKey, cancellationToken);

        var dtos = orders.Select(o => new OrderDto(
            OrderRef: o.OrderRef,
            OrderNumber: o.OrderNumber,
            Status: o.Status.ToString(),
            CustomerName: o.CustomerName,
            CustomerEmail: o.CustomerEmail,
            Total: o.Total,
            TotalFormatted: _priceFormatter.Format(o.Total, o.Currency),
            Currency: o.Currency,
            Date: o.CreatedAt,
            CreatedAt: o.CreatedAt,
            Items: o.Lines.Select(ToOrderLineDto).ToList())).ToList();

        return Ok(new OrdersResponse(Orders: dtos));
    }

    // ── 6. Wishlist / listas (IUserCollection, seam genérico P11) ──
    // GET  /api/shop/wishlist?owner=<email>&collection=<nombre?>  → items
    // GET  /api/shop/wishlist/collections?owner=<email>           → resumen de listas
    // POST /api/shop/wishlist { owner, collection?, itemRef }     → agrega (idempotente)
    // DELETE /api/shop/wishlist?owner=&collection=&itemRef=       → quita
    [HttpGet("wishlist")]
    public async Task<IActionResult> Wishlist(
        [FromQuery] string? owner,
        [FromQuery] string? collection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return BadRequest(new { error = "El parámetro 'owner' (email) es requerido." });
        }

        var name = string.IsNullOrWhiteSpace(collection) ? DefaultCollection : collection.Trim();
        var items = await _collections.GetAsync(owner.Trim(), name, cancellationToken);
        return Ok(new WishlistResponse(
            Owner: owner.Trim(),
            Collection: name,
            Items: items.Select(ToCollectionItemDto).ToList()));
    }

    [HttpGet("wishlist/collections")]
    public async Task<IActionResult> WishlistCollections(
        [FromQuery] string? owner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return BadRequest(new { error = "El parámetro 'owner' (email) es requerido." });
        }

        var summaries = await _collections.GetCollectionsAsync(owner.Trim(), cancellationToken);
        return Ok(new CollectionsResponse(
            Owner: owner.Trim(),
            Collections: summaries
                .Select(s => new CollectionSummaryDto(s.Collection, s.Count, s.UpdatedAt))
                .ToList()));
    }

    [HttpPost("wishlist")]
    public async Task<IActionResult> WishlistAdd(
        [FromBody] WishlistItemRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Owner)
            || string.IsNullOrWhiteSpace(request.ItemRef))
        {
            return BadRequest(new { error = "owner e itemRef son requeridos." });
        }

        var name = string.IsNullOrWhiteSpace(request.Collection) ? DefaultCollection : request.Collection.Trim();
        var item = await _collections.AddAsync(request.Owner.Trim(), name, request.ItemRef.Trim(), cancellationToken);
        return Ok(ToCollectionItemDto(item));
    }

    [HttpDelete("wishlist")]
    public async Task<IActionResult> WishlistRemove(
        [FromQuery] string? owner,
        [FromQuery] string? collection,
        [FromQuery] string? itemRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(itemRef))
        {
            return BadRequest(new { error = "owner e itemRef son requeridos." });
        }

        var name = string.IsNullOrWhiteSpace(collection) ? DefaultCollection : collection.Trim();
        var removed = await _collections.RemoveAsync(owner.Trim(), name, itemRef.Trim(), cancellationToken);
        return Ok(new { removed });
    }

    // ── 7. Tracking de la orden (IOrderTrackingService, seam genérico P4) ──
    // GET /api/shop/order/{orderRef}/tracking → etapas + fechas + etapa actual.
    // La orden alimenta el timeline al confirmar el pago (etapa "paid"); las
    // etapas siguientes las mueve el fulfillment/vendedor. Orden Pending →
    // timeline aún no iniciado (stages vacías, currentStage null).
    [HttpGet("order/{orderRef}/tracking")]
    public async Task<IActionResult> OrderTracking(string orderRef, CancellationToken cancellationToken)
    {
        var order = await _orders.GetOrderAsync(orderRef, cancellationToken);
        if (order is null)
        {
            return NotFound(new { error = "Orden no encontrada." });
        }
        if (DenyIfForeignMember(order) is { } forbidden)
        {
            return forbidden;
        }

        var timeline = await _tracking.GetTimelineAsync(order.OrderRef, cancellationToken);
        return Ok(new TrackingResponse(
            OrderRef: order.OrderRef,
            OrderNumber: order.OrderNumber,
            OrderStatus: order.Status.ToString(),
            CurrentStage: timeline?.CurrentStage,
            Stages: timeline?.Stages
                .Select(s => new TrackingStageDto(
                    s.Stage, s.Label,
                    // done si ya se alcanzó; current si es la etapa activa; pending si no.
                    State: s.Reached ? "done" : (s.Stage == timeline.CurrentStage ? "current" : "pending"),
                    Date: s.ReachedAt,
                    s.Reached, s.ReachedAt, s.Note))
                .ToList() ?? new List<TrackingStageDto>()));
    }

    // ── 8. Devoluciones / RMA (IReturnService) ────────────────────
    // POST /api/shop/order/{orderRef}/return { lineId, reason } → abre el RMA
    // GET  /api/shop/order/{orderRef}/return                    → RMAs de la orden
    // POST /api/shop/return/{rmaId}/advance { status, note? }   → mueve el RMA
    //      (aprobada/rechazada/recibida/reembolsada — reembolso vía PSP)
    [HttpPost("order/{orderRef}/return")]
    public async Task<IActionResult> RequestReturn(
        string orderRef,
        [FromBody] ReturnRequestBody? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.LineId) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "lineId y reason son requeridos." });
        }

        var order = await _orders.GetOrderAsync(orderRef, cancellationToken);
        if (order is null)
        {
            return NotFound(new { error = "Orden no encontrada." });
        }
        if (DenyIfForeignMember(order) is { } forbidden)
        {
            return forbidden;
        }

        try
        {
            var rma = await _returns.RequestAsync(order.OrderRef, request.LineId.Trim(), request.Reason.Trim(), cancellationToken);
            return Ok(ToReturnDto(rma));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("order/{orderRef}/return")]
    public async Task<IActionResult> ReturnsForOrder(string orderRef, CancellationToken cancellationToken)
    {
        var order = await _orders.GetOrderAsync(orderRef, cancellationToken);
        if (order is null)
        {
            return NotFound(new { error = "Orden no encontrada." });
        }
        if (DenyIfForeignMember(order) is { } forbidden)
        {
            return forbidden;
        }

        var cases = await _returns.GetForOrderAsync(order.OrderRef, cancellationToken);
        return Ok(new ReturnsResponse(Returns: cases.Select(ToReturnDto).ToList()));
    }

    [HttpPost("return/{rmaId}/advance")]
    public async Task<IActionResult> AdvanceReturn(
        string rmaId,
        [FromBody] ReturnAdvanceBody? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(new { error = "status es requerido (approved|rejected|received|refunded)." });
        }
        if (!Enum.TryParse<ShopReturnStatus>(request.Status.Trim(), ignoreCase: true, out var target))
        {
            return BadRequest(new { error = $"Estado '{request.Status}' inválido." });
        }

        try
        {
            var rma = await _returns.AdvanceAsync(rmaId, target, request.Note, cancellationToken);
            return Ok(ToReturnDto(rma));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Transición ilegal o reembolso no procesable.
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── 9. Mensajería comprador↔vendedor (IMessagingService, P7 v1) ──
    // POST /api/shop/messages { contextRef, from, to, body }  → inicia/retoma hilo
    // POST /api/shop/messages/{threadId}/reply { from, body } → responde
    // GET  /api/shop/messages/{threadId}                      → hilo completo
    // GET  /api/shop/messages?participant=<email>             → bandeja
    [HttpPost("messages")]
    public async Task<IActionResult> StartThread(
        [FromBody] StartThreadRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.ContextRef)
            || string.IsNullOrWhiteSpace(request.From)
            || string.IsNullOrWhiteSpace(request.To)
            || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "contextRef, from, to y body son requeridos." });
        }

        try
        {
            var thread = await _messaging.StartThreadAsync(
                request.ContextRef.Trim(), request.From.Trim(), request.To.Trim(), request.Body, cancellationToken);
            return Ok(ToThreadDto(thread));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("messages/{threadId}/reply")]
    public async Task<IActionResult> ReplyThread(
        string threadId,
        [FromBody] ReplyRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "from y body son requeridos." });
        }

        try
        {
            var thread = await _messaging.ReplyAsync(threadId, request.From.Trim(), request.Body, cancellationToken);
            return Ok(ToThreadDto(thread));
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, "threadId", StringComparison.Ordinal))
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("messages/{threadId}")]
    public async Task<IActionResult> GetThread(string threadId, CancellationToken cancellationToken)
    {
        var thread = await _messaging.GetThreadAsync(threadId, cancellationToken);
        if (thread is null)
        {
            return NotFound(new { error = "Hilo no encontrado." });
        }
        return Ok(ToThreadDto(thread));
    }

    [HttpGet("messages")]
    public async Task<IActionResult> Inbox(
        [FromQuery] string? participant,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(participant))
        {
            return BadRequest(new { error = "El parámetro 'participant' (email) es requerido." });
        }

        var threads = await _messaging.GetInboxAsync(participant.Trim(), cancellationToken);
        return Ok(new InboxResponse(Threads: threads
            .Select(t => new ThreadSummaryDto(
                ThreadId: t.ThreadId,
                ContextRef: t.ContextRef,
                Participants: t.Participants,
                LastMessagePreview: t.LastMessagePreview,
                LastMessageAt: t.LastMessageAt,
                MessageCount: t.MessageCount))
            .ToList()));
    }

    // ── Helpers ────────────────────────────────────────────────────

    private ProductDto ToProductDto(CatalogProductSummary p) => new(
        Id: p.Id,
        Name: p.Name,
        Price: p.Price,
        PriceFormatted: _priceFormatter.Format(p.Price, p.Currency),
        Currency: p.Currency,
        Brand: p.Brand,
        Category: p.Category,
        ImageUrl: p.ImageUrl,
        Rating: p.Rating,
        ReviewCount: p.ReviewCount,
        Stock: p.Stock,
        InStock: p.Stock > 0,
        Description: null,
        ImageUrls: null,
        // La UI lee product.images (array). Emitimos ImageUrl como array de 1 (o vacío
        // → la card cae al monograma). Contrato alineado (UI = fuente de verdad).
        Images: p.ImageUrl is null ? System.Array.Empty<string>() : new[] { p.ImageUrl });

    private static CollectionItemDto ToCollectionItemDto(UserCollectionItem i) => new(
        Owner: i.Owner,
        Collection: i.Collection,
        ItemRef: i.ItemRef,
        AddedAt: i.AddedAt);

    private ReturnDto ToReturnDto(ShopReturnCase c) => new(
        ClaimId: c.RmaId,   // clave que lee la UI
        RmaId: c.RmaId,
        OrderRef: c.OrderRef,
        LineRef: c.LineRef,
        ProductName: c.ProductName,
        Quantity: c.Quantity,
        RefundAmount: c.RefundAmount,
        RefundAmountFormatted: _priceFormatter.Format(c.RefundAmount, c.Currency),
        Currency: c.Currency,
        Reason: c.Reason,
        Status: MapReturnStatusToUi(c.Status),
        RequestedAt: c.RequestedAt,
        UpdatedAt: c.UpdatedAt,
        Note: c.Note);

    // Vocabulario del estado del RMA al que lee la UI (ES). El ciclo interno
    // (Requested→Approved/Received→Refunded, o Rejected terminal) se colapsa a 3.
    private static string MapReturnStatusToUi(ShopReturnStatus s) => s switch
    {
        ShopReturnStatus.Requested => "abierto",
        ShopReturnStatus.Approved or ShopReturnStatus.Received => "en-revision",
        ShopReturnStatus.Refunded or ShopReturnStatus.Rejected => "resuelto",
        _ => "abierto",
    };

    private static ThreadDto ToThreadDto(MessageThread t) => new(
        ThreadId: t.ThreadId,
        ContextRef: t.ContextRef,
        Participants: t.Participants,
        Messages: t.Messages.Select(m => new ThreadMessageDto(m.MessageId, m.From, m.Body, m.SentAt)).ToList(),
        CreatedAt: t.CreatedAt,
        LastMessageAt: t.LastMessageAt);

    private OrderLineDto ToOrderLineDto(ShopOrderLine l) => new(
        ProductId: l.ProductId,
        VariantId: l.VariantId,
        Title: l.ProductName,   // clave que lee la UI (confirm + historial)
        Qty: l.Quantity,
        Amount: l.LineTotal,
        ProductName: l.ProductName,
        Quantity: l.Quantity,
        UnitPrice: l.UnitPrice,
        UnitPriceFormatted: _priceFormatter.Format(l.UnitPrice, l.Currency),
        LineTotal: l.LineTotal,
        LineTotalFormatted: _priceFormatter.Format(l.LineTotal, l.Currency),
        Currency: l.Currency);

    // ── Request DTOs (binding del módulo storefront) ────────────────

    /// <summary>POST /api/shop/checkout — líneas del carrito + comprador.</summary>
    public sealed record CheckoutRequest(
        IReadOnlyList<CartItemRequest>? Items,
        CustomerRequest? Customer);

    /// <summary>Una línea del carrito en el payload.</summary>
    public sealed record CartItemRequest(string ProductId, string? VariantId, int Qty);

    /// <summary>El comprador en el payload del checkout.</summary>
    public sealed record CustomerRequest(string Name, string Email);

    /// <summary>POST /api/shop/confirm — la orden a capturar/confirmar.</summary>
    public sealed record ConfirmRequest(string OrderRef);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────

    /// <summary>Producto enriquecido con precio es-CO; Description/ImageUrls solo en PDP.</summary>
    public sealed record ProductDto(
        string Id,
        string Name,
        decimal Price,
        string PriceFormatted,
        string Currency,
        string Brand,
        string Category,
        string? ImageUrl,
        double Rating,
        int ReviewCount,
        int Stock,
        bool InStock,
        string? Description,
        IReadOnlyList<string>? ImageUrls,
        IReadOnlyList<string> Images);

    /// <summary>
    /// Producto PLANO para <c>GET products/sku/{sku}</c>, con la forma del contrato
    /// <c>Product</c> de <c>@synergos/contracts</c> que lee el elemento
    /// <c>product-card</c>. Deliberadamente distinto de <see cref="ProductDto"/>:
    /// aquel no lleva <c>sku</c> y emite <c>images</c> como <c>string[]</c>.
    /// </summary>
    public sealed record ProductBySkuDto(
        string Id,
        string Sku,
        string Name,
        decimal Price,
        string PriceFormatted,
        string Currency,
        bool InStock,
        string? Url,
        string? Category,
        IReadOnlyList<ProductImageDto> Images,
        ProductRatingDto? Rating);

    /// <summary>Imagen del producto. La tarjeta lee <c>images[0].src</c> y <c>images[0].alt</c>.</summary>
    public sealed record ProductImageDto(string Src, string? Alt);

    /// <summary>
    /// Prueba social del producto (T10, ADR 0114). Calca <c>Product.rating?: {average, count}</c>
    /// de <c>@synergos/contracts</c> — la UI es la fuente de verdad de la clave (ADR 0083).
    /// </summary>
    /// <remarks>
    /// Es NULLABLE en <see cref="ProductBySkuDto"/> a propósito: sin reseñas la propiedad
    /// desaparece del JSON y la tarjeta no pinta estrella. Un objeto con ceros sería un
    /// producto valorado con la peor nota (ADR 0112).
    /// </remarks>
    public sealed record ProductRatingDto(double Average, int Count);

    public sealed record FacetDto(string Key, string Label, IReadOnlyList<FacetValueDto> Values, string Kind = "MultiSelect");

    public sealed record FacetValueDto(string Value, int Count, string? Label = null);

    public sealed record SearchResponse(
        IReadOnlyList<ProductDto> Products,
        IReadOnlyList<FacetDto> Facets,
        int Total);

    public sealed record VariantDto(
        string VariantId,
        string Name,
        string Label,
        decimal Price,
        string PriceFormatted,
        string Currency,
        int Stock,
        bool InStock);

    public sealed record ReviewDto(string Author, int Rating, string Title, string Body, DateOnly Date);

    // La UI lee `author` (sin fallback a asker) → sin él la Q&A muestra "Comprador".
    public sealed record QuestionDto(string Asker, string Author, string Question, string? Answer, bool Answered, DateOnly Date);

    public sealed record ProductDetailResponse(
        ProductDto Product,
        IReadOnlyList<VariantDto> Variants,
        IReadOnlyList<ReviewDto> Reviews,
        IReadOnlyList<QuestionDto> Questions);

    public sealed record CheckoutResponse(
        string OrderRef,
        string PaymentSessionId,
        decimal Amount,
        string AmountFormatted,
        string Currency);

    public sealed record OrderLineDto(
        string ProductId,
        string? VariantId,
        // Claves canónicas que lee la UI (ADR 0083): title/qty/amount. Sin ellas
        // el confirm filtra la línea (item sin title → null) y el historial la pinta
        // con título vacío, qty=1 y monto=0. ProductName/Quantity/LineTotal se conservan.
        string Title,
        int Qty,
        decimal Amount,
        string ProductName,
        int Quantity,
        decimal UnitPrice,
        string UnitPriceFormatted,
        decimal LineTotal,
        string LineTotalFormatted,
        string Currency);

    public sealed record ConfirmResponse(
        string Status,
        string OrderNumber,
        string OrderRef,
        decimal Total,
        string TotalFormatted,
        string Currency,
        IReadOnlyList<OrderLineDto> Items);

    public sealed record OrderDto(
        string OrderRef,
        string OrderNumber,
        string Status,
        string CustomerName,
        string CustomerEmail,
        decimal Total,
        string TotalFormatted,
        string Currency,
        // `date` es la clave que lee la UI en el historial (normalizeOrders). CreatedAt
        // se conserva para consumers previos; ambas portan el mismo instante.
        DateTimeOffset Date,
        DateTimeOffset CreatedAt,
        IReadOnlyList<OrderLineDto> Items);

    public sealed record OrdersResponse(IReadOnlyList<OrderDto> Orders);

    // ── OLA 1 Tienda T0 — wishlist / tracking / devoluciones / mensajes ──

    /// <summary>POST /api/shop/wishlist — agregar un ítem a una lista del usuario.</summary>
    public sealed record WishlistItemRequest(string Owner, string? Collection, string ItemRef);

    public sealed record CollectionItemDto(
        string Owner,
        string Collection,
        string ItemRef,
        DateTimeOffset AddedAt);

    public sealed record WishlistResponse(
        string Owner,
        string Collection,
        IReadOnlyList<CollectionItemDto> Items);

    public sealed record CollectionSummaryDto(string Collection, int Count, DateTimeOffset UpdatedAt);

    public sealed record CollectionsResponse(
        string Owner,
        IReadOnlyList<CollectionSummaryDto> Collections);

    public sealed record TrackingStageDto(
        string Stage,
        string Label,
        // `state` (done|current|pending) y `date` son lo que lee la UI (normalizeTracking);
        // se derivan de Reached + CurrentStage. Reached/ReachedAt se conservan.
        string State,
        DateTimeOffset? Date,
        bool Reached,
        DateTimeOffset? ReachedAt,
        string? Note);

    /// <summary>Timeline de la orden; stages vacías + currentStage null = tracking aún no iniciado (orden Pending).</summary>
    public sealed record TrackingResponse(
        string OrderRef,
        string OrderNumber,
        string OrderStatus,
        string? CurrentStage,
        IReadOnlyList<TrackingStageDto> Stages);

    /// <summary>POST /api/shop/order/{ref}/return — abrir un RMA sobre una línea.</summary>
    public sealed record ReturnRequestBody(string LineId, string Reason);

    /// <summary>POST /api/shop/return/{rmaId}/advance — mover el RMA de estado.</summary>
    public sealed record ReturnAdvanceBody(string Status, string? Note);

    public sealed record ReturnDto(
        // La UI lee `claimId` (?? id) y `status` en vocabulario ES {abierto,en-revision,
        // resuelto}. RmaId se conserva; Status porta el valor ES-mapeado.
        string ClaimId,
        string RmaId,
        string OrderRef,
        string LineRef,
        string ProductName,
        int Quantity,
        decimal RefundAmount,
        string RefundAmountFormatted,
        string Currency,
        string Reason,
        string Status,
        DateTimeOffset RequestedAt,
        DateTimeOffset UpdatedAt,
        string? Note);

    public sealed record ReturnsResponse(IReadOnlyList<ReturnDto> Returns);

    /// <summary>POST /api/shop/messages — iniciar (o retomar) el hilo del contexto.</summary>
    public sealed record StartThreadRequest(string ContextRef, string From, string To, string Body);

    /// <summary>POST /api/shop/messages/{threadId}/reply — responder en el hilo.</summary>
    public sealed record ReplyRequest(string From, string Body);

    public sealed record ThreadMessageDto(string MessageId, string From, string Body, DateTimeOffset SentAt);

    public sealed record ThreadDto(
        string ThreadId,
        string ContextRef,
        IReadOnlyList<string> Participants,
        IReadOnlyList<ThreadMessageDto> Messages,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastMessageAt);

    public sealed record ThreadSummaryDto(
        string ThreadId,
        string ContextRef,
        IReadOnlyList<string> Participants,
        string LastMessagePreview,
        DateTimeOffset LastMessageAt,
        int MessageCount);

    public sealed record InboxResponse(IReadOnlyList<ThreadSummaryDto> Threads);
}
