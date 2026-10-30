using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Las rutas del personal de la tienda (013-seller-operaciones): la bandeja de los pedidos pagados de la tienda entera, el
/// detalle de uno con su envío (T011) y el acto que lo despacha (T019). Las usa la consola del vendedor; la ruta del
/// comprador (<c>GET orders</c>, «Mis compras») no se toca.
/// </summary>
/// <remarks>
/// <para><b>Sólo el personal</b>, con los roles de <c>Synergos:Tienda:RolesDelPersonal</c> (el mismo valor que la RMA y
/// los flujos del personal): sin sesión, 401 con su motivo; con sesión y sin el rol, 403 sin cuerpo. La guarda va antes
/// de leer nada.</para>
///
/// <para><b>La bandeja concilia antes de leer</b> (FR-004): una compra de la puerta cobrada cuyo comprador no volvió a
/// «Mis compras» seguiría pendiente. <c>sinConciliar</c> dice cuántas no alcanzó el techo.</para>
///
/// <para><b>El envío de un pedido de la puerta lo lee el orquestador</b>, porque la dirección sólo la guarda el envío; el
/// de la directa sale de su <c>ShipTo</c>. Si no se pudo leer, <c>envio</c> es nulo con su motivo, y el pedido se ve
/// igual.</para>
///
/// <para><b>El acto decide por el camino del pedido</b> (R4): uno de la puerta envía y entrega por el orquestador
/// (<see cref="DespachosDeLaPuerta"/>) y, si sale, el seguimiento del comprador lo sigue con la guía como nota; con un
/// rechazo, el seguimiento se queda donde estaba. Preparar, y todo lo de la directa, sólo mueven el seguimiento, con su
/// asiento de auditoría: es lo que no deja rastro en una saga del orquestador (FR-002).</para>
/// </remarks>
[ApiController]
[Route("api/shop/seller")]
public sealed class VendedorDeLaTiendaController : ControllerBase
{
    /// <summary>La guía más larga que acepta el acto de enviar.</summary>
    public const int GuiaMaxima = 80;

    /// <summary>A qué etapa del seguimiento lleva cada acto.</summary>
    private static readonly IReadOnlyDictionary<string, string> EtapaDelActo = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["preparar"] = "preparing",
        ["enviar"] = "shipped",
        ["entregar"] = "delivered",
    };

    private readonly PedidosDeLaTienda _pedidos;
    private readonly RegistroDeComprasDeLaTienda _registro;
    private readonly DespachosDeLaPuerta _despachos;
    private readonly IOrderTrackingService _seguimiento;
    private readonly IAuditTrailWriter _auditoria;
    /// <summary>Cuántas publicaciones trae una página.</summary>
    public const int PublicacionesPorPagina = 50;

    private readonly IReturnService _devoluciones;
    private readonly OrdenesDeLaTienda _ordenes;
    private readonly IProductCatalogProvider _catalogo;
    private readonly ITiendaEnElContenido _tiendaEnElContenido;
    private readonly IMemberAccessGate _gate;
    private readonly IPriceFormatter _precios;
    private readonly IOptionsMonitor<TiendaSettings>? _tienda;

    public VendedorDeLaTiendaController(
        PedidosDeLaTienda pedidos, RegistroDeComprasDeLaTienda registro, DespachosDeLaPuerta despachos,
        IOrderTrackingService seguimiento, IAuditTrailWriter auditoria, IReturnService devoluciones, OrdenesDeLaTienda ordenes,
        IProductCatalogProvider catalogo, ITiendaEnElContenido tiendaEnElContenido, IMemberAccessGate gate, IPriceFormatter precios,
        IOptionsMonitor<TiendaSettings>? tienda = null)
    {
        _pedidos = pedidos;
        _registro = registro;
        _despachos = despachos;
        _seguimiento = seguimiento;
        _auditoria = auditoria;
        _devoluciones = devoluciones;
        _ordenes = ordenes;
        _catalogo = catalogo;
        _tiendaEnElContenido = tiendaEnElContenido;
        _gate = gate;
        _precios = precios;
        _tienda = tienda;
    }

    private string Personal => _tienda?.CurrentValue.PersonalCsv() ?? TiendaSettings.RolPorDefecto;

    /// <summary>
    /// La bandeja: la página <paramref name="pagina"/> (desde 1) de los pedidos pagados en <paramref name="etapa"/> (o de
    /// todos), con las cuentas por etapa de la tienda entera y cuántas compras de la puerta quedaron sin conciliar.
    /// </summary>
    [HttpGet("orders")]
    public async Task<IActionResult> Bandeja([FromQuery] string? etapa, [FromQuery] int pagina = 1, CancellationToken ct = default)
    {
        if (RequirePersonal() is { } denegado) return denegado;
        if (!string.IsNullOrWhiteSpace(etapa) && !PedidosDeLaTienda.EsEtapa(etapa.Trim()))
        {
            return BadRequest(new { code = "tienda.etapa_desconocida", error = $"«{etapa}» no es una etapa del despacho." });
        }

        var sinConciliar = await _registro.ReconciliarLaTiendaAsync(ct);
        var bandeja = await _pedidos.BandejaAsync(etapa, pagina, ct);
        return Ok(new BandejaDto(
            bandeja.Pedidos.Select(ComoDto).ToList(), bandeja.Pagina, bandeja.PorPagina, bandeja.Total, bandeja.Cuentas, sinConciliar));
    }

    /// <summary>El detalle de un pedido pagado, con su envío: a dónde va, con quién y por dónde ha ido.</summary>
    [HttpGet("orders/{orderRef}")]
    public async Task<IActionResult> Detalle(string orderRef, CancellationToken ct = default)
    {
        if (RequirePersonal() is { } denegado) return denegado;

        var pedido = await _pedidos.PedidoAsync(orderRef, ct);
        if (pedido is null) return NotFound(new { error = "Pedido no encontrado." });

        var (envio, motivo) = await EnvioDeAsync(pedido.Orden, ct);
        var dto = ComoDto(pedido);
        return Ok(new DetalleDto(
            dto.OrderRef, dto.OrderNumber, dto.Fecha, dto.Comprador, dto.Camino, dto.Etapa, dto.Total, dto.TotalFormatted,
            dto.Moneda, dto.Lineas, dto.Actos, dto.MotivoSinActos, envio, motivo));
    }

    /// <summary>
    /// El acto del personal sobre un pedido pagado: <c>preparar</c>, <c>enviar</c> (con su guía, si la hay) o
    /// <c>entregar</c>. Contesta el pedido como en la bandeja, ya en su etapa nueva.
    /// </summary>
    /// <remarks>
    /// Sólo el acto que el pedido tiene (<c>actos</c> de la bandeja): otro es 409 con la etapa en que sigue, y repetir uno
    /// ya hecho también, sin sellar el seguimiento otra vez.
    /// </remarks>
    [HttpPost("orders/{orderRef}/advance")]
    public async Task<IActionResult> Avanzar(string orderRef, [FromBody] ActoDelPedidoDto? cuerpo, CancellationToken ct = default)
    {
        if (RequirePersonal() is { } denegado) return denegado;

        var acto = cuerpo?.Acto?.Trim() ?? string.Empty;
        if (!EtapaDelActo.TryGetValue(acto, out var etapa))
        {
            return BadRequest(new { code = "tienda.acto_desconocido", error = $"«{acto}» no es un acto del despacho: preparar, enviar o entregar." });
        }
        var guia = cuerpo?.Guia is { } escrita && !string.IsNullOrWhiteSpace(escrita) ? escrita.Trim() : null;
        if (guia is not null && (acto != "enviar" || guia.Length > GuiaMaxima || guia.Any(char.IsControl)))
        {
            return BadRequest(new { code = "tienda.guia_invalida", error = $"La guía: hasta {GuiaMaxima} caracteres, en una línea, y sólo al enviar." });
        }

        var pedido = await _pedidos.PedidoAsync(orderRef, ct);
        if (pedido is null) return NotFound(new { error = "Pedido no encontrado." });
        if (!pedido.Actos.Contains(acto, StringComparer.Ordinal))
        {
            return pedido.MotivoSinActos == "despacho_no_disponible"
                ? Conflict(new { code = DespachosDeLaPuerta.NoDisponible, error = "Los pedidos de la puerta no se despachan desde este sitio.", etapa = pedido.Etapa })
                : Conflict(new { code = "tienda.acto_fuera_de_orden", error = $"El pedido está en «{pedido.Etapa}»: «{acto}» no le toca.", etapa = pedido.Etapa });
        }

        var porElOrquestador = pedido.Orden.ViaGate && acto is "enviar" or "entregar";
        if (porElOrquestador)
        {
            var despacho = acto == "enviar"
                ? await _despachos.EnviarAsync(pedido.Orden.OrderRef, guia, ct)
                : await _despachos.EntregarAsync(pedido.Orden.OrderRef, ct);
            if (!despacho.Bien) return Rechazo(despacho);
        }

        await _seguimiento.AdvanceAsync(pedido.Orden.OrderRef, etapa, guia, ct);
        if (!porElOrquestador) await AsentarAsync(pedido, acto, etapa, guia, ct);

        var movido = await _pedidos.PedidoAsync(orderRef, ct);
        return movido is null ? NotFound(new { error = "Pedido no encontrado." }) : Ok(ComoDto(movido));
    }

    /// <summary>
    /// La cola de devoluciones de la tienda entera (T024): lo que más espera, primero
    /// (<see cref="IReturnService.GetForStoreAsync"/>), con el estado EXACTO y el pedido de cada una —su número, y su
    /// camino, que es por donde volverá la plata—. Una devolución cuya orden no se encuentra sale sin número ni camino.
    /// </summary>
    [HttpGet("returns")]
    public async Task<IActionResult> Devoluciones(CancellationToken ct = default)
    {
        if (RequirePersonal() is { } denegado) return denegado;

        var casos = await _devoluciones.GetForStoreAsync(ct);
        var ordenes = (await _ordenes.TodasAsync(ct)).ToDictionary(o => o.OrderRef, StringComparer.Ordinal);
        return Ok(new DevolucionesDto(casos.Select(c =>
        {
            var orden = ordenes.GetValueOrDefault(c.OrderRef);
            return new DevolucionDto(
                c.RmaId, c.OrderRef, orden?.OrderNumber, orden is null ? null : orden.ViaGate ? "puerta" : "directa",
                c.ProductName, c.Quantity, c.Reason, c.RefundAmount, _precios.Format(c.RefundAmount, c.Currency), c.Currency,
                c.Status.ToString(), c.Note, c.RequestedAt, c.UpdatedAt);
        }).ToList()));
    }

    /// <summary>
    /// Las publicaciones de la tienda (T026, R8): las fichas como las ve el comprador —el mismo
    /// <see cref="IProductCatalogProvider"/> que <c>GET search</c>—, de a <see cref="PublicacionesPorPagina"/>, con su estado:
    /// a la venta, agotada (sin existencias) o reservable (un servicio por cita, que no se vende en el carrito, FR-026 de
    /// 004). No publica ni edita: eso es del backoffice (S-1).
    /// </summary>
    [HttpGet("products")]
    public async Task<IActionResult> Publicaciones([FromQuery] int pagina = 1, CancellationToken ct = default)
    {
        if (RequirePersonal() is { } denegado) return denegado;

        pagina = Math.Max(1, pagina);
        var desde = (int)Math.Min((long)(pagina - 1) * PublicacionesPorPagina, int.MaxValue);
        var resultado = await _catalogo.SearchAsync(new ProductQuery(Skip: desde, Take: PublicacionesPorPagina), ct);
        var reservables = _tiendaEnElContenido.SkusReservables();
        return Ok(new PublicacionesDto(
            resultado.Products.Select(p => new PublicacionDto(
                p.Id, p.Id, p.Name, p.Price, _precios.Format(p.Price, p.Currency), p.Currency, p.Stock,
                reservables.Contains(p.Id) ? "reservable" : p.Stock > 0 ? "a-la-venta" : "agotada")).ToList(),
            pagina, PublicacionesPorPagina, resultado.Total));
    }

    /// <summary>Un rechazo del despacho, con su código: lo transitorio es 503, y lo demás 409 (o la guarda de la puerta).</summary>
    private ObjectResult Rechazo(DespachoDeLaPuerta despacho) => StatusCode(
        despacho.Transitorio
            ? StatusCodes.Status503ServiceUnavailable
            : despacho.Codigo switch
            {
                "puerta.sesion_requerida" => StatusCodes.Status401Unauthorized,
                "puerta.personal_requerido" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status409Conflict,
            },
        new { code = despacho.Codigo, error = despacho.Mensaje, transient = despacho.Transitorio });

    /// <summary>El rastro de un acto que no pasó por el orquestador (FR-002): quién, sobre qué pedido y de qué etapa a cuál.</summary>
    private Task AsentarAsync(PedidoDeLaTienda pedido, string acto, string etapa, string? guia, CancellationToken ct)
    {
        var correo = _gate.CurrentMemberEmail ?? string.Empty;
        return _auditoria.WriteAsync(new AuditEvent(
            Id: Guid.NewGuid().ToString("N"),
            OccurredAtUtc: DateTime.UtcNow,
            ActorEmail: correo,
            ActorName: _gate.CurrentMemberDisplayName ?? correo,
            Action: "shop.order-dispatch",
            Resource: pedido.Orden.OrderRef,
            Outcome: "success",
            Detail: $"{acto}: {pedido.Etapa} → {etapa}" + (guia is null ? string.Empty : $" (guía {guia})"),
            Assertion: IdentityAssertions.CmsSession), ct);
    }

    private IActionResult? RequirePersonal()
    {
        if (!_gate.IsAuthenticated)
        {
            return Unauthorized(new { error = "Se requiere iniciar sesión." });
        }
        // 403 directo y no Forbid(), como RequireShopStaff: Forbid() pasa por el esquema de autenticación del sitio.
        return _gate.HasAnyRole(Personal) ? null : StatusCode(StatusCodes.Status403Forbidden);
    }

    private async Task<(EnvioDto? Envio, string? Motivo)> EnvioDeAsync(ShopOrder orden, CancellationToken ct)
    {
        if (!orden.ViaGate)
        {
            return orden.ShipTo is { } a
                ? (new EnvioDto(new DestinoDto(a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.Country, a.Contact), null, null, []), null)
                : (new EnvioDto(null, null, null, []), "sin_direccion");
        }

        var (envio, fallo) = await _registro.EnvioAsync(orden.OrderRef, ct);
        if (envio is null)
        {
            return (null, fallo is { Estado: StatusCodes.Status404NotFound } ? "sin_envio" : "envio_no_disponible");
        }
        var destino = envio.Destino is { } d ? new DestinoDto(d.Line1, d.Line2, d.City, d.Region, d.PostalCode, d.Country, d.Contact) : null;
        return (new EnvioDto(destino, envio.Transportadora, envio.Estado,
            envio.Eventos.Select(e => new EventoDto(e.Estado, e.Nota, e.Fecha)).ToList()), null);
    }

    private PedidoDto ComoDto(PedidoDeLaTienda p) => new(
        OrderRef: p.Orden.OrderRef,
        OrderNumber: p.Orden.OrderNumber,
        Fecha: p.Orden.CreatedAt,
        Comprador: p.Orden.CustomerName,
        Camino: p.Camino,
        Etapa: p.Etapa,
        Total: p.Orden.Total,
        TotalFormatted: _precios.Format(p.Orden.Total, p.Orden.Currency),
        Moneda: p.Orden.Currency,
        Lineas: p.Orden.Lines
            .Select(l => new LineaDto(l.ProductName, l.Quantity, l.LineTotal, _precios.Format(l.LineTotal, l.Currency)))
            .ToList(),
        Actos: p.Actos,
        MotivoSinActos: p.MotivoSinActos);

    // ── Lo que sale (contracts/cms-y-ui.md de 013): la UI es la fuente de las claves que lee (ADR 0083) ──

    /// <summary>
    /// Una devolución de la cola. <c>estado</c> es el exacto (<c>Requested</c>, <c>Approved</c>, <c>Received</c>,
    /// <c>Refunded</c>, <c>Rejected</c>); <c>monto</c>, lo que se devolvería (el total de la línea de la orden).
    /// </summary>
    public sealed record DevolucionDto(
        string RmaId, string OrderRef, string? OrderNumber, string? Camino, string Producto, int Cantidad, string Motivo,
        decimal Monto, string MontoFormatted, string Moneda, string Estado, string? Nota, DateTimeOffset Solicitada,
        DateTimeOffset Actualizada);

    /// <summary>La cola de devoluciones de la tienda, en su orden.</summary>
    public sealed record DevolucionesDto(IReadOnlyList<DevolucionDto> Devoluciones);

    /// <summary>
    /// Una publicación de la tienda, como la ve el comprador. <c>estado</c>: <c>a-la-venta</c>, <c>agotada</c> o
    /// <c>reservable</c>.
    /// </summary>
    public sealed record PublicacionDto(
        string Id, string Sku, string Titulo, decimal Precio, string PrecioFormatted, string Moneda, int Existencias, string Estado);

    /// <summary>Una página de las publicaciones de la tienda.</summary>
    public sealed record PublicacionesDto(IReadOnlyList<PublicacionDto> Publicaciones, int Pagina, int PorPagina, int Total);

    /// <summary>El cuerpo del acto: <c>preparar</c>, <c>enviar</c> o <c>entregar</c>, y la guía, sólo al enviar.</summary>
    public sealed record ActoDelPedidoDto(string? Acto, string? Guia);

    /// <summary>Una línea del pedido.</summary>
    public sealed record LineaDto(string Title, int Qty, decimal Amount, string AmountFormatted);

    /// <summary>Un pedido de la bandeja. <c>comprador</c> es el nombre: el correo no se muestra.</summary>
    public sealed record PedidoDto(
        string OrderRef, string OrderNumber, DateTimeOffset Fecha, string Comprador, string Camino, string Etapa,
        decimal Total, string TotalFormatted, string Moneda, IReadOnlyList<LineaDto> Lineas,
        IReadOnlyList<string> Actos, string? MotivoSinActos);

    /// <summary>Una página de la bandeja.</summary>
    public sealed record BandejaDto(
        IReadOnlyList<PedidoDto> Pedidos, int Pagina, int PorPagina, int Total, IReadOnlyDictionary<string, int> Cuentas,
        int SinConciliar);

    /// <summary>A dónde va un envío.</summary>
    public sealed record DestinoDto(
        string? Line1, string? Line2, string? City, string? Region, string? PostalCode, string? Country, string? Contact);

    /// <summary>Un paso del envío.</summary>
    public sealed record EventoDto(string Estado, string? Nota, DateTimeOffset Fecha);

    /// <summary>El envío de un pedido; en la directa, sólo su destino.</summary>
    public sealed record EnvioDto(DestinoDto? Destino, string? Transportadora, string? Estado, IReadOnlyList<EventoDto> Eventos);

    /// <summary>
    /// El pedido como en la bandeja, más su envío. <c>motivoSinEnvio</c>: <c>sin_direccion</c> (una de la directa
    /// anterior a <c>ShipTo</c>), <c>sin_envio</c> (el orquestador no tiene un envío para esa compra) o
    /// <c>envio_no_disponible</c> (no se pudo leer ahora).
    /// </summary>
    public sealed record DetalleDto(
        string OrderRef, string OrderNumber, DateTimeOffset Fecha, string Comprador, string Camino, string Etapa,
        decimal Total, string TotalFormatted, string Moneda, IReadOnlyList<LineaDto> Lineas,
        IReadOnlyList<string> Actos, string? MotivoSinActos, EnvioDto? Envio, string? MotivoSinEnvio);
}
