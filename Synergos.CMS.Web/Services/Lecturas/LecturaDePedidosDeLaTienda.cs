using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Los pedidos de la tienda en la lectura genérica (014-despiece-tienda, I4): «Mis compras» y el seguimiento de una
/// compra, con lo mismo que dicen <c>GET api/shop/orders</c> y <c>GET api/shop/order/{ref}/tracking</c>, que siguen
/// como interinas.
/// </summary>
public sealed class LecturaDePedidosDeLaTienda : ILecturaDePedidos
{
    private readonly IShopOrderService _pedidos;
    private readonly IOrderTrackingService _seguimiento;
    private readonly IReturnService _devoluciones;
    private readonly IPriceFormatter _precios;
    private readonly IOptionsMonitor<TiendaSettings> _tienda;

    /// <summary>Sobre los servicios de pedidos, seguimiento y devoluciones de la tienda.</summary>
    public LecturaDePedidosDeLaTienda(
        IShopOrderService pedidos,
        IOrderTrackingService seguimiento,
        IReturnService devoluciones,
        IPriceFormatter precios,
        IOptionsMonitor<TiendaSettings> tienda)
    {
        _pedidos = pedidos ?? throw new ArgumentNullException(nameof(pedidos));
        _seguimiento = seguimiento ?? throw new ArgumentNullException(nameof(seguimiento));
        _devoluciones = devoluciones ?? throw new ArgumentNullException(nameof(devoluciones));
        _precios = precios ?? throw new ArgumentNullException(nameof(precios));
        _tienda = tienda ?? throw new ArgumentNullException(nameof(tienda));
    }

    /// <inheritdoc />
    public string Vertical => CatalogoDeLaTienda.Vertical;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PedidoDelMiembro>> DelMiembroAsync(
        Guid miembro,
        CancellationToken cancellationToken = default)
    {
        var ordenes = await _pedidos.GetOrdersByMemberAsync(miembro, cancellationToken).ConfigureAwait(false);
        var pedidos = new List<PedidoDelMiembro>(ordenes.Count);
        foreach (var orden in ordenes)
        {
            var lineas = new List<LineaDelPedido>(orden.Lines.Count);
            foreach (var linea in orden.Lines)
            {
                lineas.Add(await LineaAsync(orden.OrderRef, linea, cancellationToken).ConfigureAwait(false));
            }

            pedidos.Add(new PedidoDelMiembro(
                Referencia: orden.OrderRef,
                Numero: orden.OrderNumber,
                Estado: await EstadoQueVeElCompradorAsync(orden, cancellationToken).ConfigureAwait(false),
                Fecha: orden.CreatedAt,
                Total: PrecioDe(orden.Total, orden.Currency),
                Lineas: lineas));
        }

        return pedidos;
    }

    /// <inheritdoc />
    public async Task<LecturaPorReferencia<SeguimientoDelPedido>> SeguimientoAsync(
        string referencia,
        IMemberAccessGate sesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        if (string.IsNullOrWhiteSpace(referencia))
        {
            return LecturaPorReferencia.NoExiste<SeguimientoDelPedido>();
        }

        var orden = await _pedidos.GetOrderAsync(referencia.Trim(), cancellationToken).ConfigureAwait(false);
        if (orden is null)
        {
            return LecturaPorReferencia.NoExiste<SeguimientoDelPedido>();
        }

        if (ReglaDelPedidoAjeno.EsAjeno(orden, sesion, ReglaDelPedidoAjeno.Personal(_tienda)))
        {
            return LecturaPorReferencia.Ajena<SeguimientoDelPedido>();
        }

        var linea = await _seguimiento.GetTimelineAsync(orden.OrderRef, cancellationToken).ConfigureAwait(false);
        var etapas = linea?.Stages
            .Select(s => new EtapaDelSeguimiento(
                Clave: s.Stage,
                Titulo: s.Label,
                Estado: s.Reached ? "hecha" : s.Stage == linea.CurrentStage ? "actual" : "pendiente",
                Fecha: s.ReachedAt,
                Nota: s.Note))
            .ToList() ?? [];

        return LecturaPorReferencia.Leida(new SeguimientoDelPedido(
            Referencia: orden.OrderRef,
            Numero: orden.OrderNumber,
            Estado: await EstadoQueVeElCompradorAsync(orden, cancellationToken).ConfigureAwait(false),
            EtapaActual: linea?.CurrentStage,
            Etapas: etapas));
    }

    /// <summary>
    /// El estado que ve quien compró: el de la orden y, ya pagada, la etapa del despacho si pasó de «pagado» (la misma
    /// que lee la bandeja del personal, <see cref="PedidosDeLaTienda.EtapaAsync"/>).
    /// </summary>
    private async Task<string> EstadoQueVeElCompradorAsync(ShopOrder orden, CancellationToken cancellationToken)
    {
        if (orden.Status != OrderStatus.Paid)
        {
            return orden.Status.ToString();
        }

        var etapa = await PedidosDeLaTienda.EtapaAsync(_seguimiento, orden.OrderRef, cancellationToken).ConfigureAwait(false);
        return etapa == StubOrderTrackingService.StagePaid ? orden.Status.ToString() : etapa;
    }

    private async Task<LineaDelPedido> LineaAsync(string referencia, ShopOrderLine linea, CancellationToken cancellationToken)
    {
        var opcion = string.IsNullOrWhiteSpace(linea.VariantId) ? null : linea.VariantId;
        var id = opcion is null ? linea.ProductId : $"{linea.ProductId}/{opcion}";
        var bloqueo = await _devoluciones.CanRequestAsync(referencia, id, cancellationToken).ConfigureAwait(false);
        return new LineaDelPedido(
            Id: id,
            ElementoId: linea.ProductId,
            OpcionId: opcion,
            Titulo: linea.ProductName,
            Cantidad: linea.Quantity,
            Unitario: PrecioDe(linea.UnitPrice, linea.Currency),
            Total: PrecioDe(linea.LineTotal, linea.Currency),
            PuedeDevolverse: bloqueo == ShopReturnBlock.None,
            MotivoSinDevolucion: MotivoDe(bloqueo));
    }

    /// <summary>Por qué una línea no se puede devolver, con el vocabulario de la lectura.</summary>
    private static string? MotivoDe(ShopReturnBlock bloqueo) => bloqueo switch
    {
        ShopReturnBlock.None => null,
        ShopReturnBlock.OrderNotFound => "pedido-inexistente",
        ShopReturnBlock.OrderNotPaid => "pedido-sin-pagar",
        ShopReturnBlock.LineNotInOrder => "linea-ajena",
        ShopReturnBlock.AlreadyOpen => "ya-abierta",
        _ => "no-disponible",
    };

    private Precio PrecioDe(decimal monto, string moneda) => new(monto, moneda, _precios.Format(monto, moneda));
}
