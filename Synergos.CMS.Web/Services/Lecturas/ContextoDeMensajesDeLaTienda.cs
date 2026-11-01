using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Los hilos de la tienda en la bandeja genérica (014-despiece-tienda, I4). Son los de contexto <c>tienda:</c>
/// (<c>tienda:pedido:{ref}</c>, <c>tienda:producto:{id}</c>) y los que abrió <c>POST api/shop/messages</c> con la
/// referencia de un pedido (<c>ord_…</c>). Los demás hilos del mismo correo (los mensajes directos de Blogs, los
/// radicados de Gobierno) no son de la tienda: la bandeja de <c>GET api/shop/messages</c>, interina, los traía.
/// </summary>
public sealed class ContextoDeMensajesDeLaTienda : IContextoDeMensajes
{
    /// <summary>El prefijo del contexto de un hilo de la tienda.</summary>
    public const string Prefijo = "tienda:";

    private const string DeUnPedido = "pedido:";
    private const string DeUnProducto = "producto:";
    private const string ReferenciaDeUnPedido = "ord_";

    private readonly IShopOrderService _pedidos;
    private readonly IEnumerable<ILecturaDelCatalogo> _catalogos;

    /// <summary>Sobre los pedidos y el catálogo de la tienda, de donde salen los asuntos.</summary>
    public ContextoDeMensajesDeLaTienda(IShopOrderService pedidos, IEnumerable<ILecturaDelCatalogo> catalogos)
    {
        _pedidos = pedidos ?? throw new ArgumentNullException(nameof(pedidos));
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
    }

    /// <inheritdoc />
    public string Vertical => CatalogoDeLaTienda.Vertical;

    /// <inheritdoc />
    public bool EsDelVertical(string contexto)
        => !string.IsNullOrWhiteSpace(contexto)
            && (contexto.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase)
                || contexto.StartsWith(ReferenciaDeUnPedido, StringComparison.Ordinal));

    /// <inheritdoc />
    public async Task<string> AsuntoAsync(string contexto, CancellationToken cancellationToken = default)
    {
        var resto = contexto.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase)
            ? contexto[Prefijo.Length..]
            : contexto;

        if (resto.StartsWith(DeUnProducto, StringComparison.OrdinalIgnoreCase))
        {
            var catalogo = _catalogos.DelVertical(Vertical);
            var productos = catalogo is null
                ? []
                : await catalogo.ElementosAsync([resto[DeUnProducto.Length..]], cancellationToken).ConfigureAwait(false);
            return productos.Count > 0 ? productos[0].Titulo : "Un producto de la tienda";
        }

        var referencia = resto.StartsWith(DeUnPedido, StringComparison.OrdinalIgnoreCase)
            ? resto[DeUnPedido.Length..]
            : resto;
        var pedido = string.IsNullOrWhiteSpace(referencia)
            ? null
            : await _pedidos.GetOrderAsync(referencia, cancellationToken).ConfigureAwait(false);
        return pedido is null ? "Conversación con la tienda" : $"Pedido {pedido.OrderNumber}";
    }
}
