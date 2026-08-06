namespace Synergos.CMS.Interfaces;

/// <summary>
/// Service para gestionar el estado del cart de compra del visitante
/// actual. Aísla a renderers Razor y controllers de la persistencia
/// concreta (cookie, sesión, DB).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultCartService</c> usando una
/// cookie HMAC-firmada. Los métodos mutators escriben la nueva cookie
/// en el HttpResponse del request actual.
///
/// El cart se reproyecta cada vez que <see cref="GetCart"/> se llama:
/// los SKUs almacenados en cookie se cruzan con <c>productPage</c>
/// publicados para hidratar nombre, precio actual e imagen. Cambios de
/// precio en CMS se reflejan inmediatamente.
///
/// Sin checkout — el cart provee estado, no procesamiento de pagos. La
/// integración con un gateway de pagos vendría en una ola futura.
/// </remarks>
public interface ICartService
{
    /// <summary>
    /// Devuelve el snapshot actual del cart hidratado con datos
    /// frescos de los productos (nombre, precio, imagen). Items con
    /// SKU que ya no existen en el CMS se omiten silenciosamente.
    /// </summary>
    Cart GetCart();

    /// <summary>
    /// Agrega un item al cart. Si el item (SKU + variantSku) ya existe,
    /// se incrementa la cantidad. Respeta <c>CartSettings.MaxItems</c>.
    /// </summary>
    /// <returns>El cart actualizado, hidratado.</returns>
    Cart AddItem(string sku, int quantity = 1, string? variantSku = null);

    /// <summary>
    /// Establece la cantidad exacta de un item del cart. Si quantity ≤ 0,
    /// equivale a <see cref="RemoveItem"/>.
    /// </summary>
    Cart UpdateQuantity(string sku, int quantity, string? variantSku = null);

    /// <summary>Remueve un item del cart si existe.</summary>
    Cart RemoveItem(string sku, string? variantSku = null);

    /// <summary>Vacía el cart completo.</summary>
    Cart Clear();
}

/// <summary>
/// Snapshot inmutable del cart actual. Producido por
/// <see cref="ICartService"/>. Plantillas Razor lo consumen sin tocar
/// cookie ni HttpContext directamente.
/// </summary>
/// <param name="Lines">Líneas del cart, hidratadas con producto actual.
///   Vacío si el cart está vacío.</param>
/// <param name="Subtotal">Suma de todas las <see cref="CartLine.LineTotal"/>.
///   En la moneda de <see cref="Currency"/>.</param>
/// <param name="Currency">ISO 4217 de la moneda del cart.</param>
/// <param name="ItemCount">Suma de las cantidades de todas las líneas.
///   No el número de líneas distintas.</param>
public sealed record Cart(
    IReadOnlyList<CartLine> Lines,
    decimal Subtotal,
    string Currency,
    int ItemCount);

/// <summary>
/// Una línea del cart, ya hidratada con datos del producto actual.
/// </summary>
/// <param name="Sku">SKU del producto base.</param>
/// <param name="VariantSku">SKU de la variante (talla/color), si aplica.</param>
/// <param name="Quantity">Cantidad de unidades.</param>
/// <param name="ProductName">Nombre actual del producto.</param>
/// <param name="UnitPrice">Precio unitario actual.</param>
/// <param name="LineTotal"><c>UnitPrice × Quantity</c>.</param>
/// <param name="ImageUrl">Primera imagen del producto, o null.</param>
/// <param name="ProductUrl">URL del productPage.</param>
public sealed record CartLine(
    string Sku,
    string? VariantSku,
    int Quantity,
    string ProductName,
    decimal UnitPrice,
    decimal LineTotal,
    string? ImageUrl,
    string ProductUrl);
