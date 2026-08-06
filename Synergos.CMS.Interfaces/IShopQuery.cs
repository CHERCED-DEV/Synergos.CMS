namespace Synergos.CMS.Interfaces;

/// <summary>
/// Query seam para listar y buscar productos del catálogo (DocType
/// <c>productPage</c>). Aísla a renderers Razor de la lógica de
/// búsqueda en el árbol Umbraco — el service consume
/// <c>IUmbracoContextAccessor</c> internamente y proyecta a
/// <see cref="ProductSummary"/> records.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultShopQuery</c>. Sin caché —
/// Umbraco mantiene published cache en memoria. Para catálogos con
/// 10k+ productos evaluar Examine o store dedicado.
/// </remarks>
public interface IShopQuery
{
    /// <summary>
    /// Devuelve los productos que cumplen el filtro, ordenados según
    /// <see cref="ShopQueryRequest.SortBy"/>.
    /// </summary>
    IReadOnlyList<ProductSummary> GetProducts(ShopQueryRequest request);

    /// <summary>
    /// Resuelve un producto por su SKU. <c>null</c> si no existe o no
    /// está publicado.
    /// </summary>
    ProductSummary? GetProductBySku(string sku);
}

/// <summary>
/// Filtros para la query de productos. Todos opcionales. Cuando todos
/// vacíos, devuelve los <c>MaxItems</c> productos del catálogo.
/// </summary>
/// <param name="MaxItems">Máximo de productos a devolver. Default 12.</param>
/// <param name="Skip">Cuántos saltar (paginación). Default 0.</param>
/// <param name="CategoryAliasOrName">Nombre de
///   <c>productCategoryPage</c> a filtrar. Vacío = todas las categorías.</param>
/// <param name="SortBy">Estrategia de orden: "name", "price-asc",
///   "price-desc", "newest". Default "name".</param>
public sealed record ShopQueryRequest(
    int MaxItems = 12,
    int Skip = 0,
    string? CategoryAliasOrName = null,
    string? SortBy = null);

/// <summary>
/// Snapshot inmutable de un producto para listings.
/// </summary>
/// <param name="Sku">SKU único del producto.</param>
/// <param name="Name">Nombre del producto.</param>
/// <param name="Price">Precio base actual.</param>
/// <param name="Currency">ISO 4217 de la moneda del catálogo.</param>
/// <param name="ImageUrl">URL de la primera imagen del producto, o null.</param>
/// <param name="Url">URL relativa del productPage.</param>
/// <param name="InStock">true si <c>productInStock</c> está activo.</param>
/// <param name="CategoryName">Nombre de la categoría padre, o null.</param>
public sealed record ProductSummary(
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string? ImageUrl,
    string Url,
    bool InStock,
    string? CategoryName);
