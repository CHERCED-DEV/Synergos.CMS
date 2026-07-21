namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una reseña de un comprador sobre un producto, tal como se ALMACENA.
/// </summary>
/// <remarks>
/// No confundir con <see cref="ProductReview"/> de <c>IProductCatalogProvider</c>: aquel es la
/// PROYECCIÓN de lectura que consume la PDP (autor como nombre visible, sin SKU ni identidad).
/// Este es la entidad, y lleva las dos cosas que aquel no puede llevar: el SKU al que pertenece
/// y el <c>memberKey</c> de quien la escribió. Al cablear, este se proyecta en aquel — y el
/// <see cref="AuthorKey"/> se queda aquí: la identidad del miembro no viaja a la vista.
/// </remarks>
/// <param name="Sku">Producto reseñado. Es también la clave de agrupación en el almacén.</param>
/// <param name="AuthorKey">
/// El <c>memberKey</c> del autor. Es SERVER-TRUSTED: sale del gate de miembros, JAMÁS del
/// cuerpo de la petición (T2 — un actorKey que viaja en el body es un IDOR con otro nombre).
/// Es además la clave de idempotencia: un comprador tiene UNA reseña por producto, y volver
/// a enviarla la EDITA en vez de duplicarla.
/// </param>
/// <param name="AuthorName">Nombre visible; se resuelve del miembro, no lo elige el cliente.</param>
/// <param name="Rating">Estrellas, 1..5. Fuera de rango se rechaza: no se recorta en silencio.</param>
public sealed record CustomerReview(
    string Sku,
    Guid AuthorKey,
    string AuthorName,
    int Rating,
    string Title,
    string Body,
    DateOnly Date);

/// <summary>
/// El agregado de reseñas de un producto: la forma que la UI lee
/// (<c>Product.rating?: {average, count}</c> de <c>@synergos/contracts</c>).
/// </summary>
/// <remarks>
/// <b>Es DERIVADO: se calcula al leer y no se almacena nunca.</b> Un contador persistido se
/// desincroniza del día que una reseña se edita o se borra, y entonces miente sin que nada
/// falle. La fuente de verdad son las reseñas; esto es una vista de ellas.
/// </remarks>
public sealed record ProductSocialProof(string Sku, double Average, int Count);

/// <summary>
/// Prueba social del catálogo (T10, doc <c>26</c>): reseñas de compradores y el rating que
/// se deriva de ellas.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque el rating es <b>UGC derivado, no contenido editorial</b>: un editor no autora
/// la reseña de un comprador. Hasta ahora <c>UmbracoProductCatalogSource</c> emitía
/// <c>Rating: 0d</c> fijo con un comentario prometiendo este seam — y esa promesa llevaba
/// tiempo sin cumplirse, así que TODO el catálogo de contenido valía 0.
/// </para>
/// <para>
/// <b>Ausencia, no cero.</b> <see cref="GetAsync"/> devuelve <c>null</c> cuando el producto no
/// tiene reseñas — no un agregado con ceros. Un "0,0" pintado en cada tarjeta es exactamente
/// la columna muerta que el proyecto ya arregló una vez en las facetas (ADR 0112).
/// </para>
/// </remarks>
public interface ICatalogSocialProof
{
    /// <summary>
    /// El agregado del producto, o <c>null</c> si no tiene reseñas (degradar por AUSENCIA).
    /// </summary>
    Task<ProductSocialProof?> GetAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>Las reseñas del producto, de la más reciente a la más antigua. Vacío si no hay.</summary>
    Task<IReadOnlyList<CustomerReview>> GetReviewsAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta o edición de la reseña. Idempotente por <see cref="CustomerReview.AuthorKey"/>:
    /// el mismo autor sobre el mismo SKU sustituye la suya, no añade otra.
    /// </summary>
    Task UpsertReviewAsync(CustomerReview review, CancellationToken cancellationToken = default);
}
