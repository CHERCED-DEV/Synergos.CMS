using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Las devoluciones de un pedido de la tienda en la lectura genérica (014-despiece-tienda, I4): las RMA de
/// <see cref="IReturnService"/>, la misma que el personal lleva a «reembolsada» por <c>tienda.devolucion</c> (NS-3).
/// Lo mismo que dice <c>GET api/shop/order/{ref}/return</c>, que sigue como interina.
/// </summary>
public sealed class LecturaDeDevolucionesDeLaTienda : ILecturaDeDevoluciones
{
    private readonly IShopOrderService _pedidos;
    private readonly IReturnService _devoluciones;
    private readonly IPriceFormatter _precios;
    private readonly IOptionsMonitor<TiendaSettings> _tienda;

    /// <summary>Sobre los servicios de pedidos y devoluciones de la tienda.</summary>
    public LecturaDeDevolucionesDeLaTienda(
        IShopOrderService pedidos,
        IReturnService devoluciones,
        IPriceFormatter precios,
        IOptionsMonitor<TiendaSettings> tienda)
    {
        _pedidos = pedidos ?? throw new ArgumentNullException(nameof(pedidos));
        _devoluciones = devoluciones ?? throw new ArgumentNullException(nameof(devoluciones));
        _precios = precios ?? throw new ArgumentNullException(nameof(precios));
        _tienda = tienda ?? throw new ArgumentNullException(nameof(tienda));
    }

    /// <inheritdoc />
    public string Vertical => CatalogoDeLaTienda.Vertical;

    /// <inheritdoc />
    public async Task<LecturaPorReferencia<DevolucionesDelPedido>> DelPedidoAsync(
        string referencia,
        IMemberAccessGate sesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        if (string.IsNullOrWhiteSpace(referencia))
        {
            return LecturaPorReferencia.NoExiste<DevolucionesDelPedido>();
        }

        var orden = await _pedidos.GetOrderAsync(referencia.Trim(), cancellationToken).ConfigureAwait(false);
        if (orden is null)
        {
            return LecturaPorReferencia.NoExiste<DevolucionesDelPedido>();
        }

        if (ReglaDelPedidoAjeno.EsAjeno(orden, sesion, ReglaDelPedidoAjeno.Personal(_tienda)))
        {
            return LecturaPorReferencia.Ajena<DevolucionesDelPedido>();
        }

        var casos = await _devoluciones.GetForOrderAsync(orden.OrderRef, cancellationToken).ConfigureAwait(false);
        return LecturaPorReferencia.Leida(new DevolucionesDelPedido(
            Referencia: orden.OrderRef,
            Devoluciones: casos
                .Select(c => new DevolucionDelPedido(
                    Id: c.RmaId,
                    Linea: c.LineRef,
                    Titulo: c.ProductName,
                    Cantidad: c.Quantity,
                    Monto: new Precio(c.RefundAmount, c.Currency, _precios.Format(c.RefundAmount, c.Currency)),
                    Motivo: c.Reason,
                    Estado: EstadoDe(c.Status),
                    Solicitada: c.RequestedAt,
                    Actualizada: c.UpdatedAt,
                    Nota: c.Note))
                .ToList()));
    }

    private static string EstadoDe(ShopReturnStatus estado) => estado switch
    {
        ShopReturnStatus.Approved => EstadosDeLaDevolucion.Aprobada,
        ShopReturnStatus.Rejected => EstadosDeLaDevolucion.Rechazada,
        ShopReturnStatus.Received => EstadosDeLaDevolucion.Recibida,
        ShopReturnStatus.Refunded => EstadosDeLaDevolucion.Reembolsada,
        _ => EstadosDeLaDevolucion.Solicitada,
    };
}
