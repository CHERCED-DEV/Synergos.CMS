namespace Synergos.CMS.Interfaces;

/// <summary>
/// Consulta del catálogo del marketplace (dominio Tienda). Filtra/busca el
/// catálogo (texto + categoría + facetas) y resuelve el detalle de un producto
/// (variantes + reviews + preguntas/Q&amp;A). Es la pieza del MOTOR que resuelve
/// "qué productos hay" para la PLP/búsqueda y "qué tiene este producto" para la
/// PDP — el equivalente e-commerce de <see cref="IRoomAvailabilityProvider"/>
/// (Hoteles) / <see cref="IFlightAvailabilityProvider"/> (Aerolíneas).
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IPaymentProvider"/> y el resto del
/// motor): el default <c>StubProductCatalogProvider</c> (Application, lógica
/// pura/determinista) sirve un catálogo sembrado en memoria (varias categorías ×
/// productos × variantes × reviews × preguntas) para que la demo del marketplace
/// corra end-to-end; el adapter real (Examine/Lucene sobre <c>productPage</c>,
/// o Algolia/Elastic) se enchufa después sin tocar el módulo Angular ni el
/// controller. ADR 0002 (Application sin Umbraco) + ADR 0075 (seam con tests).
///
/// Aditivo: coexiste con <c>IShopQuery</c> (filtro plano Umbraco-backed para los
/// bloques Razor del CMS) sin reemplazarlo — esta seam es la fuente de la app
/// real (búsqueda facetada + PDP rica) que <c>IShopQuery</c> no cubre.
/// </remarks>
public interface IProductCatalogProvider
{
    /// <summary>
    /// Busca/filtra el catálogo según <paramref name="query"/> (texto libre +
    /// categoría + facetas seleccionadas) y devuelve los productos que cumplen,
    /// junto con las facetas derivadas (conteos por categoría/marca/rating) que
    /// la PLP usa para refinar. Nunca lanza por filtro vacío: catálogo vacío
    /// devuelve <c>Products = []</c> + <c>Facets = []</c>.
    /// </summary>
    Task<ProductSearchResult> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve el detalle de un producto por su id (PDP): el producto + sus
    /// variantes (talla/color → SKU/precio/stock) + reviews (rating + texto) +
    /// preguntas y respuestas. Devuelve <c>null</c> si el producto no existe.
    /// </summary>
    Task<ProductDetail?> GetProductAsync(string productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filtros de la búsqueda del catálogo. Todos opcionales: <see cref="Text"/>
/// (texto libre sobre nombre/marca), <see cref="Category"/> (categoría exacta),
/// <see cref="Facets"/> (facetas seleccionadas, e.g. <c>brand=Acme</c>,
/// <c>condition=new</c>). Vacío = todo el catálogo. <see cref="Sort"/> ordena el
/// resultado ("relevance"|"price-asc"|"price-desc"|"rating"|"newest").
/// </summary>
public sealed record ProductQuery(
    string? Text = null,
    string? Category = null,
    IReadOnlyDictionary<string, string>? Facets = null,
    string? Sort = null);

/// <summary>
/// Resultado de <see cref="IProductCatalogProvider.SearchAsync"/>: los productos
/// que pasan el filtro + las facetas derivadas (con conteos) para refinar la PLP.
/// </summary>
public sealed record ProductSearchResult(
    IReadOnlyList<CatalogProductSummary> Products,
    IReadOnlyList<ProductFacet> Facets,
    int Total);

/// <summary>
/// Snapshot de un producto del marketplace para listings (PLP/búsqueda). Es la
/// unidad que la grilla de resultados renderiza (con <c>product-card</c> del
/// cluster Angular). Distinto del <c>ProductSummary</c> de <c>IShopQuery</c>
/// (filtro plano Umbraco-backed para los bloques Razor del CMS): este trae
/// marca + rating + reviewCount + stock para la búsqueda facetada del app real.
/// </summary>
public sealed record CatalogProductSummary(
    string Id,
    string Name,
    decimal Price,
    string Currency,
    string Brand,
    string Category,
    string? ImageUrl,
    double Rating,
    int ReviewCount,
    int Stock);

/// <summary>
/// Una faceta derivada del catálogo para refinar la búsqueda (e.g. categoría,
/// marca, rating). <see cref="Field"/> es el nombre canónico que la UI manda de
/// vuelta en <see cref="ProductQuery.Facets"/>; <see cref="Values"/> son los
/// valores posibles con su conteo de productos.
/// </summary>
public sealed record ProductFacet(
    string Field,
    string Label,
    IReadOnlyList<ProductFacetValue> Values);

/// <summary>Un valor de faceta + cuántos productos lo tienen (post-filtro de texto/categoría).</summary>
public sealed record ProductFacetValue(string Value, int Count);

/// <summary>
/// Detalle completo de un producto (PDP): el producto + variantes + reviews +
/// preguntas. Lo devuelve <see cref="IProductCatalogProvider.GetProductAsync"/>.
/// </summary>
public sealed record ProductDetail(
    CatalogProductSummary Product,
    string Description,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<ProductVariant> Variants,
    IReadOnlyList<ProductReview> Reviews,
    IReadOnlyList<ProductQuestion> Questions);

/// <summary>
/// Una variante reservable del producto (talla/color → SKU propio). El checkout
/// aparta stock por <see cref="VariantId"/>; <see cref="Price"/> puede diferir
/// del precio base del producto (delta por variante).
/// </summary>
public sealed record ProductVariant(
    string VariantId,
    string Name,
    decimal Price,
    string Currency,
    int Stock);

/// <summary>Una review de un comprador: autor + rating (1-5) + texto + fecha.</summary>
public sealed record ProductReview(
    string Author,
    int Rating,
    string Title,
    string Body,
    DateOnly Date);

/// <summary>
/// Una pregunta al vendedor (Q&amp;A) + su respuesta si ya fue contestada
/// (<see cref="Answer"/> = null mientras está pendiente).
/// </summary>
public sealed record ProductQuestion(
    string Asker,
    string Question,
    string? Answer,
    DateOnly Date);
