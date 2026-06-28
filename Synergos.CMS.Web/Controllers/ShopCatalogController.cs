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
    private readonly IProductCatalogProvider _catalog;
    private readonly IShopOrderService _orders;
    private readonly IPriceFormatter _priceFormatter;

    public ShopCatalogController(
        IProductCatalogProvider catalog,
        IShopOrderService orders,
        IPriceFormatter priceFormatter)
    {
        _catalog = catalog;
        _orders = orders;
        _priceFormatter = priceFormatter;
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
        var facetDtos = result.Facets.Select(f => new FacetDto(
            Field: f.Field,
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

    public sealed record FacetDto(string Field, string Label, IReadOnlyList<FacetValueDto> Values);

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
}
