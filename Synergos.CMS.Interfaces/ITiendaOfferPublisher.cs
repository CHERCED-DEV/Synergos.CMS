namespace Synergos.CMS.Interfaces;

/// <summary>Cómo terminó publicar o retirar la oferta de un producto de la tienda.</summary>
public enum TiendaOfferOutcome
{
    /// <summary>El orquestador la aplicó: publicada, el producto se vende por él; retirada, ya no.</summary>
    Published,

    /// <summary>No hay orquestador de la tienda configurado: no se intentó, y no es un fallo.</summary>
    NoDestination,

    /// <summary>Se intentó y no se pudo. Quedó en el log; se repara republicando.</summary>
    Failed,
}

/// <summary>
/// Publica la oferta de un producto del catálogo —precio y existencias de cada variante— en el orquestador de
/// la tienda, que la deja en las capacidades (004-tienda-compra, R-9, FR-023).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> La compra por el orquestador (la de la puerta y la del motor imperativo)
/// cotiza contra <c>Api.Pricing</c> y aparta contra <c>Api.Inventory</c>, y el catálogo vive en el
/// contenido. Sin publicar la oferta, lo que el catálogo muestra no es lo que se cobra.</para>
///
/// <para><b>Nunca lanza por el orquestador.</b> Lo llama el publicar del editor, que no puede morir porque
/// otro proceso esté caído: el contenido ya se guardó. Devuelve cómo terminó; la reparación es republicar.</para>
///
/// <para><b>Nunca al arrancar</b> (ADR 0013): se dispara al publicar, al retirar o a mano.</para>
/// </remarks>
public interface ITiendaOfferPublisher
{
    /// <summary>Publica la oferta del producto tal como lo sirve el catálogo.</summary>
    /// <remarks>La oferta es el estado ENTERO del producto: una variante que ya no viene deja de venderse.</remarks>
    Task<TiendaOfferOutcome> PublishAsync(ProductDetail producto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retira la oferta de un producto: ninguna de sus variantes se vende más por el orquestador. Es lo que
    /// pasa al despublicarlo, mandarlo a la papelera o borrarlo, y cuando el catálogo deja de servirlo.
    /// </summary>
    /// <param name="productId">El identificador del producto (su SKU).</param>
    /// <param name="cancellationToken">Cancelación de quien llama.</param>
    Task<TiendaOfferOutcome> RetireAsync(string productId, CancellationToken cancellationToken = default);
}
