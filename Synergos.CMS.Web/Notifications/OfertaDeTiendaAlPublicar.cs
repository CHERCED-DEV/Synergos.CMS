using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar un <c>productPage</c>, publica su oferta en el orquestador de la tienda (004-tienda-compra, R-9).
/// El molde de <see cref="OfertaDeEventoAlPublicar"/>.
/// </summary>
/// <remarks>
/// <para><b>Sólo con el catálogo en el contenido</b> (<c>Synergos:Catalog:Sources:Shop = cms</c>): en
/// <c>demo</c> el catálogo es la siembra y un <c>productPage</c> no se vende. La siembra se publica a mano,
/// con <c>POST /api/shop/oferta/republicar</c>.</para>
///
/// <para><b>Lee el producto por el catálogo y no del contenido que llega</b>: la oferta sale de la MISMA
/// proyección que pinta la ficha (con el precio ya leído como precio, no como texto). Un producto que el
/// catálogo omite (sin SKU, sin un precio inequívoco) no tiene ficha, y tampoco oferta: la que tuviera se
/// RETIRA, para que no se siga vendiendo lo que la ficha ya no muestra.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre. Lo que falle queda en
/// el log, y se repara republicando.</para>
/// </remarks>
public sealed class OfertaDeTiendaAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    internal const string ProductPageAlias = "productPage";

    internal const string PropiedadDelSku = "productSku";

    private readonly IProductCatalogProvider _catalogo;
    private readonly ITiendaOfferPublisher _ofertas;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeTiendaAlPublicar> _log;

    public OfertaDeTiendaAlPublicar(
        IProductCatalogProvider catalogo,
        ITiendaOfferPublisher ofertas,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeTiendaAlPublicar> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _fuentes = fuentes;
        _log = log;
    }

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var productos = notification.PublishedEntities
            .Where(c => string.Equals(c.ContentType.Alias, ProductPageAlias, StringComparison.Ordinal))
            .ToList();
        if (productos.Count == 0 || !DelContenido(_fuentes)) return;

        foreach (var contenido in productos)
        {
            var sku = contenido.GetValue<string>(PropiedadDelSku)?.Trim();
            if (string.IsNullOrEmpty(sku)) continue;

            try
            {
                var producto = await _catalogo.GetProductAsync(sku, cancellationToken).ConfigureAwait(false);
                if (producto is null)
                {
                    _log.LogWarning(
                        "Se publicó el producto {Sku} y el catálogo no lo sirve: se retira su oferta. Ver el log del catálogo.", sku);
                    await _ofertas.RetireAsync(sku, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _ofertas.PublishAsync(producto, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del producto {Sku}. Se repara republicando.", sku);
            }
        }
    }

    /// <summary>Si los productos salen del contenido: sólo entonces un <c>productPage</c> se vende.</summary>
    internal static bool DelContenido(IOptionsMonitor<CatalogSettings> fuentes)
        => fuentes.CurrentValue.Sources.TryGetValue(UmbracoProductCatalogSource.Vertical, out var fuente)
           && string.Equals(fuente, "cms", StringComparison.OrdinalIgnoreCase);
}
