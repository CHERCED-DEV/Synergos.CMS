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
/// <param name="Facets">
/// Facetas seleccionadas, <b>con una LISTA de valores por clave</b>: OR dentro de la misma
/// clave, AND entre claves distintas.
/// </param>
/// <param name="Skip">Ítems a saltar (paginación). Se capa a rango válido.</param>
/// <param name="Take">Tamaño de página. Se capa a <c>CatalogSettings.MaxTake</c>.</param>
/// <param name="Scope">
/// El siteRoot del catálogo, para acotar la FUENTE. Null = sin acotar.
/// </param>
/// <remarks>
/// <b><see cref="Facets"/> lleva una lista, y ése era el bug.</b> Era
/// <c>IReadOnlyDictionary&lt;string, string&gt;</c> —un valor por clave— así que el tipo NO
/// PODÍA expresar "brand IN (Aurora, Barista)" y el multi-select se perdía en la frontera,
/// en silencio: verificado en vivo, <c>?brand=Aurora</c> → 1, <c>?brand=Barista</c> → 1,
/// <c>?brand=Aurora,Barista</c> → <b>0</b>. Filtrar por dos marcas devolvía menos que por
/// una. La UI siempre mandó una lista (<c>DiscoveryCriteria.facets</c> es
/// <c>Record&lt;string, readonly string[]&gt;</c>); era el backend quien la estrechaba.
/// </remarks>
public sealed record ProductQuery(
    string? Text = null,
    string? Category = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Facets = null,
    string? Sort = null,
    int Skip = 0,
    int Take = 24,
    string? Scope = null);

/// <summary>
/// Resultado de <see cref="IProductCatalogProvider.SearchAsync"/>: los productos
/// que pasan el filtro + las facetas derivadas (con conteos) para refinar la PLP.
/// </summary>
/// <param name="Products">Los productos de ESTA página.</param>
/// <param name="Total">
/// Cuántos productos pasan el filtro <b>en total</b>, no cuántos trae esta página: es el
/// número con el que la UI calcula cuántas páginas hay.
/// </param>
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
/// <param name="Kind">
/// Cómo se comporta el filtro (<c>MultiSelect</c>|<c>SingleSelect</c>|<c>Threshold</c>|
/// <c>Range</c>), para que la UI sepa si pintar checkboxes o radios. String y no enum: es un
/// valor de wire, y un Kind nuevo no debe romper un cliente viejo.
/// </param>
public sealed record ProductFacet(
    string Field,
    string Label,
    IReadOnlyList<ProductFacetValue> Values,
    string Kind = "MultiSelect");

/// <summary>Un valor de faceta + cuántos productos lo tienen (post-filtro de texto/categoría).</summary>
/// <param name="Label">
/// Cómo se le muestra al usuario. Null = usar <paramref name="Value"/>. Existe porque no
/// siempre coinciden: <c>minRating</c> vale <c>"4"</c> y se lee "4 estrellas o más" — sin
/// esto el chip se pintaba con el número crudo.
/// </param>
public sealed record ProductFacetValue(string Value, int Count, string? Label = null);

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
