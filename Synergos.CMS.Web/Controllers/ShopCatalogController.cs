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

    private readonly IProductCatalogProvider _catalog;
    private readonly IShopOrderService _orders;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IUserCollection _collections;
    private readonly IOrderTrackingService _tracking;
    private readonly IReturnService _returns;
    private readonly IMessagingService _messaging;

    public ShopCatalogController(
        IProductCatalogProvider catalog,
        IShopOrderService orders,
        IPriceFormatter priceFormatter,
        IUserCollection collections,
        IOrderTrackingService tracking,
        IReturnService returns,
        IMessagingService messaging)
    {
        _catalog = catalog;
        _orders = orders;
        _priceFormatter = priceFormatter;
        _collections = collections;
        _tracking = tracking;
        _returns = returns;
        _messaging = messaging;
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
        CancellationToken cancellationToken)
    {
        var facets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(brand))
        {
            facets["brand"] = brand.Trim();
        }
        if (!string.IsNullOrWhiteSpace(minRating))
        {
            facets["minRating"] = minRating.Trim();
        }

        var result = await _catalog.SearchAsync(
            new ProductQuery(
                Text: q,
                Category: category,
                Facets: facets.Count > 0 ? facets : null,
                Sort: sort),
            cancellationToken);

        var products = result.Products.Select(ToProductDto).ToList();
        // La UI (shop-api.client.ts:411 normalizeFacets) lee entry['key']; el backend
        // reshapea a esa clave (UI = fuente de verdad). Antes emitía 'field' → las
        // facetas se descartaban silenciosamente y el PLP quedaba sin filtros.
        var facetDtos = result.Facets.Select(f => new FacetDto(
            Key: f.Field,
            Label: f.Label,
            Values: f.Values.Select(v => new FacetValueDto(v.Value, v.Count)).ToList())).ToList();

        return Ok(new SearchResponse(Products: products, Facets: facetDtos, Total: result.Total));
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

        var product = ToProductDto(detail.Product) with { Description = detail.Description, ImageUrls = detail.ImageUrls };
        var variants = detail.Variants.Select(v => new VariantDto(
            VariantId: v.VariantId,
            Name: v.Name,
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

        ShopCheckoutResult result;
        try
        {
            result = await _orders.CheckoutAsync(
                items,
                new ShopCustomer(request.Customer.Name.Trim(), request.Customer.Email.Trim()),
                cancellationToken);
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
    // GET /api/shop/orders?customer=<email> → { orders:[...] }
    [HttpGet("orders")]
    public async Task<IActionResult> Orders(
        [FromQuery] string? customer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customer))
        {
            return BadRequest(new { error = "El parámetro 'customer' (email) es requerido." });
        }

        var orders = await _orders.GetOrdersAsync(customer.Trim(), cancellationToken);

        var dtos = orders.Select(o => new OrderDto(
            OrderRef: o.OrderRef,
            OrderNumber: o.OrderNumber,
            Status: o.Status.ToString(),
            CustomerName: o.CustomerName,
            CustomerEmail: o.CustomerEmail,
            Total: o.Total,
            TotalFormatted: _priceFormatter.Format(o.Total, o.Currency),
            Currency: o.Currency,
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

        var timeline = await _tracking.GetTimelineAsync(order.OrderRef, cancellationToken);
        return Ok(new TrackingResponse(
            OrderRef: order.OrderRef,
            OrderNumber: order.OrderNumber,
            OrderStatus: order.Status.ToString(),
            CurrentStage: timeline?.CurrentStage,
            Stages: timeline?.Stages
                .Select(s => new TrackingStageDto(s.Stage, s.Label, s.Reached, s.ReachedAt, s.Note))
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
        ImageUrls: null);

    private static CollectionItemDto ToCollectionItemDto(UserCollectionItem i) => new(
        Owner: i.Owner,
        Collection: i.Collection,
        ItemRef: i.ItemRef,
        AddedAt: i.AddedAt);

    private ReturnDto ToReturnDto(ShopReturnCase c) => new(
        RmaId: c.RmaId,
        OrderRef: c.OrderRef,
        LineRef: c.LineRef,
        ProductName: c.ProductName,
        Quantity: c.Quantity,
        RefundAmount: c.RefundAmount,
        RefundAmountFormatted: _priceFormatter.Format(c.RefundAmount, c.Currency),
        Currency: c.Currency,
        Reason: c.Reason,
        Status: c.Status.ToString(),
        RequestedAt: c.RequestedAt,
        UpdatedAt: c.UpdatedAt,
        Note: c.Note);

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
        IReadOnlyList<string>? ImageUrls);

    public sealed record FacetDto(string Key, string Label, IReadOnlyList<FacetValueDto> Values);

    public sealed record FacetValueDto(string Value, int Count);

    public sealed record SearchResponse(
        IReadOnlyList<ProductDto> Products,
        IReadOnlyList<FacetDto> Facets,
        int Total);

    public sealed record VariantDto(
        string VariantId,
        string Name,
        decimal Price,
        string PriceFormatted,
        string Currency,
        int Stock,
        bool InStock);

    public sealed record ReviewDto(string Author, int Rating, string Title, string Body, DateOnly Date);

    public sealed record QuestionDto(string Asker, string Question, string? Answer, bool Answered, DateOnly Date);

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
