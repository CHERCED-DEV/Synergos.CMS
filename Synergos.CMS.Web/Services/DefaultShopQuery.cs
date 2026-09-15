using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IShopQuery"/>. Recorre los descendientes del
/// siteRoot del request actual buscando nodos <c>productPage</c>,
/// aplica filtros del <see cref="ShopQueryRequest"/> y proyecta a
/// <see cref="ProductSummary"/>.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IUmbracoContextAccessor"/>. Sin caché — published cache
/// de Umbraco lo cubre. La moneda viene de
/// <c>CartSettings.Currency</c> (default COP).
/// </remarks>
public sealed class DefaultShopQuery : IShopQuery
{
    private const string ProductPageAlias = "productPage";
    private const string ProductCategoryPageAlias = "productCategoryPage";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly string _currency;

    public DefaultShopQuery(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptions<CartSettings> cartSettings)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _currency = cartSettings.Value.Currency;
    }

    public IReadOnlyList<ProductSummary> GetProducts(ShopQueryRequest request)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return Array.Empty<ProductSummary>();
        }

        // Trampa .Root() vs AncestorOrSelf("siteRoot"): .Root() devuelve el
        // platformRoot (umbrella) → DescendantsOrSelfOfType barrería TODOS los
        // siteRoots (Tienda + Propiedades + Booking…), mezclando productos con
        // inmuebles y servicios. Scopeamos al siteRoot ACTUAL del request.
        var siteRoots = umbracoContext.PublishedRequest?.PublishedContent?.AncestorOrSelf("siteRoot") is { } currentRoot
            ? new[] { currentRoot }
            : umbracoContext.Content?.GetAtRoot().ToArray() ?? Array.Empty<IPublishedContent>();

        IEnumerable<IPublishedContent> products = siteRoots
            .SelectMany(root => root.DescendantsOrSelfOfType(ProductPageAlias));

        // Category filter
        if (!string.IsNullOrWhiteSpace(request.CategoryAliasOrName))
        {
            var needle = request.CategoryAliasOrName.Trim();
            products = products.Where(p =>
            {
                var category = p.Parent;
                if (category is null || !string.Equals(category.ContentType.Alias, ProductCategoryPageAlias, StringComparison.Ordinal))
                {
                    return false;
                }
                var categoryName = category.Value<string>("categoryName") ?? category.Name;
                return string.Equals(category.Name, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryName, needle, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Un producto cuyo precio no se puede leer NO se lista: cero es un precio válido y no
        // falla en ninguna parte hasta que alguien compra (#123). Se filtra ANTES de ordenar —
        // con el respaldo a 0m, `price-asc` lo ponía primero.
        var withPrice = products
            .Select(p => (Product: p, Price: ParsePrice(p)))
            .Where(x => x.Price is not null)
            .Select(x => (x.Product, Price: x.Price!.Value));
        var sortBy = (request.SortBy ?? "name").ToLowerInvariant();
        var sorted = sortBy switch
        {
            "price-asc"  => withPrice.OrderBy(x => x.Price).ThenBy(x => x.Product.Name, StringComparer.OrdinalIgnoreCase),
            "price-desc" => withPrice.OrderByDescending(x => x.Price).ThenBy(x => x.Product.Name, StringComparer.OrdinalIgnoreCase),
            "newest"     => withPrice.OrderByDescending(x => x.Product.UpdateDate).ThenBy(x => x.Product.Name, StringComparer.OrdinalIgnoreCase),
            _            => withPrice.OrderBy(x => x.Product.Name, StringComparer.OrdinalIgnoreCase),
        };

        var maxItems = request.MaxItems > 0 ? request.MaxItems : 12;
        var skip = request.Skip > 0 ? request.Skip : 0;

        return sorted
            .Skip(skip)
            .Take(maxItems)
            .Select(x => Project(x.Product, x.Price))
            .ToArray();
    }

    public ProductSummary? GetProductBySku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            return null;
        }

        // Scopear al siteRoot actual (evita colisión de SKU entre siteRoots que
        // reusan productPage); fallback global si no hay contexto de siteRoot.
        var scopeRoots = umbracoContext.PublishedRequest?.PublishedContent?.AncestorOrSelf("siteRoot") is { } sr
            ? new[] { sr }
            : umbracoContext.Content.GetAtRoot().ToArray();
        var match = scopeRoots
            .SelectMany(r => r.DescendantsOrSelfOfType(ProductPageAlias))
            .FirstOrDefault(p => string.Equals(p.Value<string>("productSku"), sku, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return null;
        }

        // Mismo criterio que el listado: sin precio legible el producto no existe para la
        // vitrina. Devolver la ficha con Price = 0 es lo que dejaba comprar a cero (#123).
        var price = ParsePrice(match);
        return price is null ? null : Project(match, price.Value);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private ProductSummary Project(IPublishedContent product, decimal price)
    {
        var sku = product.Value<string>("productSku") ?? string.Empty;
        var name = product.Value<string>("productName") ?? product.Name ?? sku;
        var imageUrl = MediaPickerReader.ReadFirstMediaUrl(product, "productImages");
        var inStock = product.Value<bool>("productInStock");
        var category = product.Parent;
        var categoryName = category is { } c && string.Equals(c.ContentType.Alias, ProductCategoryPageAlias, StringComparison.Ordinal)
            ? (c.Value<string>("categoryName") ?? c.Name)
            : null;

        return new ProductSummary(
            Sku: sku,
            Name: name,
            Price: price,
            Currency: _currency,
            ImageUrl: imageUrl,
            Url: product.Url(),
            InStock: inStock,
            CategoryName: categoryName);
    }

    /// <summary>
    /// El precio del producto, o <c>null</c> si su <c>productPriceBase</c> no es inequívocamente
    /// uno. <b>Null es «no se sabe», y el producto no se lista.</b>
    /// </summary>
    /// <remarks>
    /// <para>Devolvía <c>decimal</c> con respaldo a <c>0m</c> (#123), así que <c>"49.000"</c> se
    /// listaba a 49 y <c>"$89000"</c> a cero. La regla de qué texto es un precio vive en
    /// <see cref="PrecioAutorado"/>; lo que se decide acá es que un producto sin precio legible
    /// no se sirve — <b>la misma política que <c>UmbracoProductCatalogSource</c></b>, porque son
    /// las dos caras de la misma vitrina y que una liste lo que la otra omite sería peor que la
    /// duplicación.</para>
    ///
    /// <para><b>Y aquí el cero además reordenaba la lista:</b> este precio es la clave de
    /// <c>price-asc</c>, así que un producto ilegible se iba de cabeza al primer puesto, que es
    /// donde más se mira.</para>
    /// </remarks>
    private static decimal? ParsePrice(IPublishedContent product)
    {
        var raw = product.Value<string>("productPriceBase");
        return PrecioAutorado.EsInequivoco(raw, out var price) && price > 0m ? price : null;
    }
}
