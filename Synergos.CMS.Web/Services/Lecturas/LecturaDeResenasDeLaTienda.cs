using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Las reseñas de un producto en la lectura genérica (014-despiece-tienda, I4): las que el catálogo de la tienda le da
/// al producto, en su orden, y si quien lee lo compró y puede reseñarlo (la regla de
/// <c>POST api/shop/products/{sku}/reviews</c>). Es lo mismo que mostraba <c>GET api/shop/product/{id}</c>.
/// </summary>
/// <remarks>
/// Las reseñas son las del CATÁLOGO y no se le suman las de <see cref="ICatalogSocialProof"/>: la fuente del contenido
/// ya pone en cada producto las de sus compradores, y sumarlas otra vez las duplicaba (se vio en vivo en la piloto).
/// De <see cref="ICatalogSocialProof"/> sale sólo <c>verificada</c>: la reseña la escribió alguien que lo compró.
/// </remarks>
public sealed class LecturaDeResenasDeLaTienda : ILecturaDeResenas
{
    private readonly IProductCatalogProvider _catalogo;
    private readonly ICatalogSocialProof _compradores;
    private readonly IShopOrderService _pedidos;

    /// <summary>Sobre el catálogo, las reseñas de los compradores y los pedidos de la tienda.</summary>
    public LecturaDeResenasDeLaTienda(
        IProductCatalogProvider catalogo,
        ICatalogSocialProof compradores,
        IShopOrderService pedidos)
    {
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _compradores = compradores ?? throw new ArgumentNullException(nameof(compradores));
        _pedidos = pedidos ?? throw new ArgumentNullException(nameof(pedidos));
    }

    /// <inheritdoc />
    public string Vertical => CatalogoDeLaTienda.Vertical;

    /// <inheritdoc />
    public async Task<ResenasDelElemento?> DelElementoAsync(
        string id,
        Guid? miembro,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var ficha = await _catalogo.GetProductAsync(id.Trim(), cancellationToken).ConfigureAwait(false);
        if (ficha is null)
        {
            return null;
        }

        var sku = ficha.Product.Id;
        var deCompradores = await _compradores.GetReviewsAsync(sku, cancellationToken).ConfigureAwait(false);
        var verificadas = deCompradores.Select(r => (r.AuthorName, r.Date, r.Title, r.Body)).ToHashSet();
        var resenas = ficha.Reviews
            .Select(r => new ResenaDelElemento(
                r.Author,
                r.Rating,
                r.Title,
                r.Body,
                r.Date,
                Verificada: verificadas.Contains((r.Author, r.Date, r.Title, r.Body))))
            .ToList();

        return new ResenasDelElemento(
            Id: sku,
            Resumen: resenas.Count == 0
                ? null
                : new Calificacion(Math.Round(resenas.Average(r => r.Calificacion), 1), resenas.Count),
            Resenas: resenas,
            PuedeResenar: miembro is Guid quien && await LoComproAsync(quien, sku, cancellationToken).ConfigureAwait(false));
    }

    private async Task<bool> LoComproAsync(Guid miembro, string sku, CancellationToken cancellationToken)
    {
        var ordenes = await _pedidos.GetOrdersByMemberAsync(miembro, cancellationToken).ConfigureAwait(false);
        return ordenes.Any(o => o.Status == OrderStatus.Paid
            && o.Lines.Any(l => string.Equals(l.ProductId, sku, StringComparison.OrdinalIgnoreCase)));
    }
}
