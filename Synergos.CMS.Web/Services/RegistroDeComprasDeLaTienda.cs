using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>Lo que contesta el registro: la orden, o el motivo con su código.</summary>
public sealed record ResultadoDelRegistro(int Estado, string? Codigo = null, string? Mensaje = null, ShopOrder? Orden = null, bool Transitorio = false)
{
    public bool Bien => Estado is >= 200 and < 300;
}

/// <summary>Una compra de la puerta del miembro, todavía sin pagar en su orden, con el estado que dice el orquestador.</summary>
public sealed record CompraPendienteDeLaTienda(string Id, string Numero, decimal Total, string Moneda, string Estado);

/// <summary>A dónde va el envío de una compra de la puerta: lo que el comprador dio al cerrar, y que sólo guarda el envío.</summary>
public sealed record DestinoDelEnvio(
    string? Line1, string? Line2, string? City, string? Region, string? PostalCode, string? Country, string? Contact);

/// <summary>Un paso del envío: su estado, su nota (la guía) y cuándo pasó.</summary>
public sealed record EventoDelEnvio(string Estado, string? Nota, DateTimeOffset Fecha);

/// <summary>El envío de una compra de la puerta, como lo lee el personal para despacharla (013-seller-operaciones, T010).</summary>
/// <param name="Estado">El de la capacidad: <c>Ready</c>, <c>InTransit</c>, <c>Delivered</c>, <c>Failed</c> o <c>Returned</c>.</param>
public sealed record EnvioDeLaTienda(
    string Id, DestinoDelEnvio? Destino, string? Transportadora, string Estado, IReadOnlyList<EventoDelEnvio> Eventos);

/// <summary>
/// El registro del comprador de la tienda (004-tienda-compra, decisión 004-P2, molde <see cref="ArtefactoDeEventos"/>):
/// lee la compra de la puerta al orquestador, con el sujeto del miembro de la sesión, y la anota en el libro de
/// órdenes para que «Mis compras», el seguimiento, la devolución y las reseñas la vean.
/// </summary>
/// <remarks>
/// <para><b>Siempre de quien compró.</b> Cada lectura lleva el sujeto del miembro —el mismo que pone la puerta—,
/// así que una compra ajena es un 404 del orquestador; y el libro sólo toca órdenes de la puerta de ese miembro.</para>
///
/// <para><b>Lo que se anota sale del lado que cobró</b>: las líneas y sus precios son la <c>salida</c> del flujo
/// (<c>lineasDelPedido</c>), nunca lo que diga el navegador. Del catálogo sólo sale el nombre del producto.</para>
///
/// <para><b>Reconciliar corre en la ruta de lectura, con techo</b> (la regla de la F3): como mucho
/// <see cref="TopeDeReconciliacion"/> órdenes, de a cuatro, y <see cref="PresupuestoDeReconciliacion"/> en
/// total; lo que falte se mira en la próxima lectura.</para>
/// </remarks>
public sealed class RegistroDeComprasDeLaTienda
{
    /// <summary>Cliente nombrado hacia el orquestador de la tienda, por la pieza del árbol.</summary>
    public const string ClientName = "synergos-bff-tienda-registro";

    /// <summary>El flujo de la puerta cuyas compras anota: de su configuración sale el <c>Kind</c> del sujeto.</summary>
    public const string Flujo = "tienda.compra";

    /// <summary>Cuántas órdenes pendientes mira, como mucho, una reconciliación.</summary>
    public const int TopeDeReconciliacion = 10;

    /// <summary>Cuánto puede tardar, como mucho, una reconciliación entera.</summary>
    public static readonly TimeSpan PresupuestoDeReconciliacion = TimeSpan.FromSeconds(3);

    private const string Cabecera = "X-Synergos-Sujeto";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clientes;
    private readonly LibroDeComprasDeLaPuerta _libro;
    private readonly IOptionsMonitor<PuertaSettings> _puerta;
    private readonly IProductCatalogProvider _catalogo;
    private readonly ILogger<RegistroDeComprasDeLaTienda> _log;
    private readonly bool _hayDestino;
    private readonly TimeSpan _presupuesto;

    /// <param name="hayDestino">Si el despliegue dice dónde vive el orquestador de la tienda.</param>
    public RegistroDeComprasDeLaTienda(
        IHttpClientFactory clientes, LibroDeComprasDeLaPuerta libro, IOptionsMonitor<PuertaSettings> puerta,
        IProductCatalogProvider catalogo, ILogger<RegistroDeComprasDeLaTienda> log, bool hayDestino, TimeSpan? presupuesto = null)
    {
        _clientes = clientes;
        _libro = libro;
        _puerta = puerta;
        _catalogo = catalogo;
        _log = log;
        _hayDestino = hayDestino;
        _presupuesto = presupuesto ?? PresupuestoDeReconciliacion;
    }

    private string? Kind => _puerta.CurrentValue.Flujos.TryGetValue(Flujo, out var f) ? f.SujetoKind : null;

    /// <summary>Tras abrir: anota la compra en curso del miembro como orden pendiente.</summary>
    public async Task<ResultadoDelRegistro> AnotarAsync(string compraId, Guid miembro, string? nombre, string? correo, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return cerrado;
        var (compra, fallo) = await LeerAsync(compraId, miembro, ct).ConfigureAwait(false);
        if (fallo is not null) return fallo;
        if (compra!.Estado is not ("Running" or "Completed"))
        {
            return new ResultadoDelRegistro(StatusCodes.Status409Conflict, "tienda.compra_no_vigente",
                compra.Error ?? "La compra se deshizo: no hay nada que anotar.");
        }

        var orden = compra.Estado == "Completed"
            ? await _libro.ConfirmarAsync(await DelLibroAsync(compra, miembro, nombre, correo, ct).ConfigureAwait(false), ct).ConfigureAwait(false)
            : await _libro.AnotarPendienteAsync(await DelLibroAsync(compra, miembro, nombre, correo, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return orden is null ? Ajena() : new ResultadoDelRegistro(StatusCodes.Status200OK, Orden: orden);
    }

    /// <summary>
    /// Tras cerrar, y desde el enlace del aviso: si la compra se completó, su orden queda pagada (una vez); si se
    /// deshizo, se descarta; si sigue en curso, se dice.
    /// </summary>
    public async Task<ResultadoDelRegistro> CompraAsync(string compraId, Guid miembro, string? nombre, string? correo, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return cerrado;
        var previa = await _libro.LeerAsync(compraId, ct).ConfigureAwait(false);
        if (previa is { Status: OrderStatus.Paid } && previa.OwnerMemberKey == miembro)
        {
            // Lo pagado no toca la red: un orquestador caído no deja a nadie sin ver su compra.
            return new ResultadoDelRegistro(StatusCodes.Status200OK, Orden: previa);
        }

        var (compra, fallo) = await LeerAsync(compraId, miembro, ct).ConfigureAwait(false);
        if (fallo is not null)
        {
            if (fallo.Codigo == "tienda.purchase_not_found") await _libro.DescartarAsync(compraId, miembro, ct).ConfigureAwait(false);
            return fallo;
        }

        switch (compra!.Estado)
        {
            case "Completed":
                var orden = await _libro.ConfirmarAsync(await DelLibroAsync(compra, miembro, nombre, correo, ct).ConfigureAwait(false), ct)
                    .ConfigureAwait(false);
                return orden is null ? Ajena() : new ResultadoDelRegistro(StatusCodes.Status200OK, Orden: orden);
            case "Running":
                return new ResultadoDelRegistro(StatusCodes.Status409Conflict, "tienda.compra_en_curso",
                    "La compra todavía no se confirmó.");
            case "Compensated":
                await _libro.DescartarAsync(compraId, miembro, ct).ConfigureAwait(false);
                return new ResultadoDelRegistro(StatusCodes.Status409Conflict, "tienda.compra_deshecha",
                    compra.Error ?? "La compra se deshizo: no se cobró.");
            default:
                // Deshaciéndose o rendida: no se afirma nada, y la orden queda como está.
                return new ResultadoDelRegistro(StatusCodes.Status409Conflict, "tienda.compra_deshaciendose",
                    "La compra se está deshaciendo.", Transitorio: true);
        }
    }

    /// <summary>Las compras de la puerta del miembro cuya orden sigue pendiente, con el estado que dice el orquestador.</summary>
    public async Task<(IReadOnlyList<CompraPendienteDeLaTienda> Compras, ResultadoDelRegistro? Fallo)> PendientesAsync(Guid miembro, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return ([], cerrado);
        var pendientes = (await _libro.DelMiembroAsync(miembro, ct).ConfigureAwait(false))
            .Where(o => o.Status == OrderStatus.Pending).Take(TopeDeReconciliacion).ToList();
        var estados = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        await ConTechoAsync(pendientes, async (o, t) =>
        {
            var (compra, _) = await LeerAsync(o.OrderRef, miembro, t).ConfigureAwait(false);
            estados[o.OrderRef] = compra?.Estado ?? "Unknown";
        }, ct).ConfigureAwait(false);

        return (pendientes
            .Select(o => new CompraPendienteDeLaTienda(o.OrderRef, o.OrderNumber, o.Total, o.Currency,
                estados.TryGetValue(o.OrderRef, out var e) ? e : "Unknown"))
            .Where(c => c.Estado != "Compensated")
            .ToList(), null);
    }

    /// <summary>
    /// Confirma las órdenes pendientes del miembro cuya compra el orquestador completó, y descarta las deshechas o
    /// inexistentes. Lo pagado no se mira. Corre al leer «Mis compras», con techo.
    /// </summary>
    public async Task ReconciliarAsync(Guid miembro, CancellationToken ct)
    {
        if (NoDisponible() is not null || miembro == Guid.Empty) return;
        var pendientes = (await _libro.DelMiembroAsync(miembro, ct).ConfigureAwait(false))
            .Where(o => o.Status == OrderStatus.Pending).Take(TopeDeReconciliacion).ToList();
        await ConTechoAsync(pendientes, async (o, t) => await ConciliarAsync(o, miembro, t).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// La conciliación de la tienda entera (013-seller-operaciones, T008, FR-004): la de <see cref="ReconciliarAsync"/>
    /// sobre las compras de la puerta pendientes de TODOS los miembros, con el mismo techo. Corre al leer la bandeja
    /// del personal.
    /// </summary>
    /// <remarks>
    /// <b>Cada compra se lee con el sujeto de SU dueño</b>, nunca con el de quien abre la bandeja: el orquestador sólo
    /// se la enseña a quien compró, y su 404 se toma como «no existe» y descarta la orden. Leerla con otro sujeto
    /// cancelaría compras cobradas.
    /// </remarks>
    /// <returns>Cuántas pendientes quedaron sin mirar: las que pasan del techo, las que no alcanzó el presupuesto y
    /// las que el orquestador no contestó.</returns>
    public async Task<int> ReconciliarLaTiendaAsync(CancellationToken ct)
    {
        var pendientes = await _libro.PendientesDeLaTiendaAsync(ct).ConfigureAwait(false);
        if (pendientes.Count == 0) return 0;
        if (NoDisponible() is not null) return pendientes.Count;

        var miradas = 0;
        await ConTechoAsync(pendientes.Take(TopeDeReconciliacion).ToList(), async (o, t) =>
        {
            if (await ConciliarAsync(o, o.OwnerMemberKey!.Value, t).ConfigureAwait(false)) Interlocked.Increment(ref miradas);
        }, ct).ConfigureAwait(false);
        return pendientes.Count - Volatile.Read(ref miradas);
    }

    /// <summary>
    /// El envío de una compra de la puerta (013-seller-operaciones, T010): a dónde va, con quién y por dónde ha ido. Lo
    /// lee del orquestador (<c>GET /v1/envios/{compra}</c>, el interino de NS-2) con el cliente del registro y sin sujeto:
    /// la ruta no tiene dueño, y el rol del personal lo comprueba quien llama.
    /// </summary>
    /// <returns>El envío, o el motivo: 404 <c>tienda.compra_no_encontrada</c> si no hay una compra completada y despachada;
    /// transitorio si el orquestador o la capacidad de envíos no contestan.</returns>
    public async Task<(EnvioDeLaTienda? Envio, ResultadoDelRegistro? Fallo)> EnvioAsync(string compra, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return (null, cerrado);
        if (string.IsNullOrWhiteSpace(compra) || Puerta.ReenvioDeLaPuerta.EsSegmentoDePunto(compra)) return (null, SinEnvio());

        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/envios/{Uri.EscapeDataString(compra)}");
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var leido = await res.Content.ReadFromJsonAsync<EnvioLeido>(Json, ct).ConfigureAwait(false);
                return leido?.Envio is null ? (null, Invalida()) : (leido.Envio, null);
            }
            if (res.StatusCode == HttpStatusCode.NotFound) return (null, SinEnvio());

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
            _log.LogWarning("La tienda contestó {Estado} ({Codigo}) al leer el envío de una compra.", (int)res.StatusCode, rechazo?.Codigo ?? "-");
            return (null, rechazo is { EsTransitorio: true }
                ? new ResultadoDelRegistro(StatusCodes.Status503ServiceUnavailable, rechazo.Codigo ?? "tienda.envio_no_disponible",
                    "El envío no se pudo leer ahora.", Transitorio: true)
                : Invalida());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("La tienda no respondió al leer el envío de una compra: {Motivo}", ex.Message);
            return (null, new ResultadoDelRegistro(StatusCodes.Status503ServiceUnavailable, "tienda.orquestador_no_disponible",
                "El orquestador de la tienda no está disponible.", Transitorio: true));
        }
    }

    private static ResultadoDelRegistro SinEnvio()
        => new(StatusCodes.Status404NotFound, "tienda.compra_no_encontrada", "La compra no tiene un envío que leer.");

    /// <summary>
    /// Concilia una orden pendiente con lo que dice el orquestador: completada, se paga; deshecha o inexistente, se
    /// descarta; en curso, se queda. Si el orquestador contestó por ella (la miró).
    /// </summary>
    private async Task<bool> ConciliarAsync(ShopOrder o, Guid miembro, CancellationToken t)
    {
        var (compra, fallo) = await LeerAsync(o.OrderRef, miembro, t).ConfigureAwait(false);
        if (compra is { Estado: "Completed" })
        {
            await _libro.ConfirmarAsync(await DelLibroAsync(compra, miembro, o.CustomerName, o.CustomerEmail, t).ConfigureAwait(false), t)
                .ConfigureAwait(false);
        }
        else if (compra is { Estado: "Compensated" } || fallo is { Codigo: "tienda.purchase_not_found" })
        {
            await _libro.DescartarAsync(o.OrderRef, miembro, t).ConfigureAwait(false);
        }
        return compra is not null || fallo is { Codigo: "tienda.purchase_not_found" };
    }

    private async Task ConTechoAsync(IReadOnlyList<ShopOrder> ordenes, Func<ShopOrder, CancellationToken, Task> una, CancellationToken ct)
    {
        if (ordenes.Count == 0) return;
        using var presupuesto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        presupuesto.CancelAfter(_presupuesto);
        try
        {
            await Parallel.ForEachAsync(ordenes,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = presupuesto.Token },
                async (o, t) => await una(o, t).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("La lectura de compras de la tienda llegó a su techo de {Techo}: lo que falta se mira en la próxima.", _presupuesto);
        }
    }

    private async Task<CompraDeLaPuerta> DelLibroAsync(CompraLeida compra, Guid miembro, string? nombre, string? correo, CancellationToken ct)
    {
        var lineas = new List<LineaDeLaCompra>();
        foreach (var l in compra.Datos?.LineasDelPedido ?? [])
        {
            var id = l.Sujeto?.Id ?? string.Empty;
            var corte = id.IndexOf(':', StringComparison.Ordinal);
            var (producto, variante) = corte < 0 ? (id, (string?)null) : (id[..corte], id[(corte + 1)..]);
            lineas.Add(new LineaDeLaCompra(producto, variante, await NombreAsync(producto, variante, ct).ConfigureAwait(false),
                l.Cantidad, l.PrecioUnitario?.Amount ?? 0m));
        }
        var total = compra.Datos?.Total;
        return new CompraDeLaPuerta(compra.Id, compra.Numero, miembro, nombre, correo, lineas, total?.Amount ?? 0m, total?.Currency ?? string.Empty);
    }

    /// <summary>Cómo se llama el producto en el catálogo; si ya no está, su id —nunca se inventa otro nombre—.</summary>
    private async Task<string> NombreAsync(string producto, string? variante, CancellationToken ct)
    {
        try
        {
            var ficha = await _catalogo.GetProductAsync(producto, ct).ConfigureAwait(false);
            if (ficha is null) return producto;
            var v = variante is null ? null : ficha.Variants.FirstOrDefault(x => string.Equals(x.VariantId, variante, StringComparison.OrdinalIgnoreCase));
            return v is null ? ficha.Product.Name : $"{ficha.Product.Name} — {v.Name}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "El catálogo no dijo el nombre de {Producto}: la orden lleva su id.", producto);
            return producto;
        }
    }

    private ResultadoDelRegistro? NoDisponible()
        => _hayDestino && !string.IsNullOrWhiteSpace(Kind)
            ? null
            : new ResultadoDelRegistro(StatusCodes.Status503ServiceUnavailable, "tienda.registro_no_disponible",
                "La compra por la puerta no está abierta en este sitio.");

    private static ResultadoDelRegistro Ajena()
        => new(StatusCodes.Status404NotFound, "tienda.purchase_not_found", "La compra no existe.");

    /// <summary>La compra tal como la ve su comprador, o el motivo por el que no se pudo leer.</summary>
    private async Task<(CompraLeida? Compra, ResultadoDelRegistro? Fallo)> LeerAsync(string compraId, Guid miembro, CancellationToken ct)
    {
        // Un segmento de punto no es una compra: escaparlo no lo cambia y movería la lectura a otra ruta.
        if (string.IsNullOrWhiteSpace(compraId) || Puerta.ReenvioDeLaPuerta.EsSegmentoDePunto(compraId)) return (null, Ajena());

        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/{Flujo}/{Uri.EscapeDataString(compraId)}");
        req.Headers.TryAddWithoutValidation(Cabecera, $"{Kind}:{miembro:n}");
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var compra = await res.Content.ReadFromJsonAsync<CompraLeida>(Json, ct).ConfigureAwait(false);
                return compra is null ? (null, Invalida()) : (compra, null);
            }
            if (res.StatusCode == HttpStatusCode.NotFound) return (null, Ajena());

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
            _log.LogWarning("La tienda contestó {Estado} ({Codigo}) al leer una compra para su registro.", (int)res.StatusCode, rechazo?.Codigo ?? "-");
            return (null, Invalida());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("La tienda no respondió al leer una compra para su registro: {Motivo}", ex.Message);
            return (null, new ResultadoDelRegistro(StatusCodes.Status503ServiceUnavailable, "tienda.orquestador_no_disponible",
                "El orquestador de la tienda no está disponible.", Transitorio: true));
        }
    }

    private static ResultadoDelRegistro Invalida()
        => new(StatusCodes.Status502BadGateway, "tienda.respuesta_invalida", "El orquestador no pudo contestar por la compra.");

    /// <summary>Lo que el registro lee de <c>RespuestaDeFlujo</c> (el contrato de <c>Synergos.Bff.Tienda.json</c>).</summary>
    internal sealed record CompraLeida(string Id, string Numero, string Estado, DatosDeLaCompra? Datos, string? Error);

    internal sealed record DatosDeLaCompra(IReadOnlyList<LineaLeida>? LineasDelPedido, DineroLeido? Total);

    internal sealed record LineaLeida(SujetoLeido? Sujeto, int Cantidad, DineroLeido? PrecioUnitario);

    internal sealed record SujetoLeido(string Kind, string Id);

    internal sealed record DineroLeido(decimal Amount, string Currency);

    /// <summary>Lo que el registro lee de <c>EnvioDeLaCompraResponse</c> (<c>LeerEnvioDeLaCompra</c>).</summary>
    internal sealed record EnvioLeido(string Compra, EnvioDeLaTienda Envio);
}
