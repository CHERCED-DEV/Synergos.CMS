using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>Un pedido pagado de la tienda, como lo ve el personal (013-seller-operaciones, data-model).</summary>
/// <param name="Orden">La orden del almacén, de cualquiera de los dos caminos.</param>
/// <param name="Camino"><c>puerta</c> o <c>directa</c>: lo dice el servidor, no la consola.</param>
/// <param name="Etapa">La del seguimiento que lee el comprador: <c>paid</c>, <c>preparing</c>, <c>shipped</c> o
/// <c>delivered</c>.</param>
/// <param name="Actos">Lo que se puede pedir ahora; la consola no lo deduce.</param>
/// <param name="MotivoSinActos">Por qué no hay actos, cuando no los hay: <c>entregado</c> o
/// <c>despacho_no_disponible</c>.</param>
public sealed record PedidoDeLaTienda(
    ShopOrder Orden, string Camino, string Etapa, IReadOnlyList<string> Actos, string? MotivoSinActos);

/// <summary>Una página de la bandeja, con las cuentas por etapa de la tienda entera.</summary>
/// <param name="Total">Cuántos pedidos hay con el filtro, en todas las páginas.</param>
/// <param name="Cuentas">Cuántos hay en cada etapa en toda la tienda: no dependen del filtro ni de la página.</param>
public sealed record BandejaDeLaTienda(
    IReadOnlyList<PedidoDeLaTienda> Pedidos, int Pagina, int PorPagina, int Total, IReadOnlyDictionary<string, int> Cuentas);

/// <summary>
/// La bandeja del personal (013-seller-operaciones, R7, FR-003): los pedidos pagados de la tienda entera, de los dos
/// caminos, del más reciente al más antiguo y de a <see cref="PorPagina"/>, cada uno en su etapa y con los actos que
/// admite (R4).
/// </summary>
/// <remarks>
/// <para><b>Sólo los pagados.</b> Un pedido pendiente todavía no se cobró, y uno cancelado no se cobró nunca: ninguno
/// se despacha.</para>
///
/// <para><b>La etapa es la del seguimiento</b> que lee el comprador. Un pagado sin seguimiento está en
/// <c>paid</c>: el seguimiento se siembra después de cobrar, y si no llegó a sembrarse la orden sigue pagada.</para>
///
/// <para><b>Los actos salen de acá.</b> <c>paid</c> → preparar, <c>preparing</c> → enviar, <c>shipped</c> → entregar,
/// <c>delivered</c> → ninguno. Uno de la puerta no tiene ninguno si la puerta no abre el despacho en este sitio
/// (<see cref="FlujoDelEnvio"/> y <see cref="FlujoDeLaEntrega"/>): su envío lo mueve el orquestador, y en este sitio
/// no hay cómo.</para>
/// </remarks>
public sealed class PedidosDeLaTienda
{
    /// <summary>Cuántos pedidos trae una página.</summary>
    public const int PorPagina = 50;

    /// <summary>El flujo posterior que despacha un pedido de la puerta.</summary>
    public const string FlujoDelEnvio = "tienda.envio";

    /// <summary>El flujo posterior que lo da por entregado.</summary>
    public const string FlujoDeLaEntrega = "tienda.entrega";

    /// <summary>Las etapas del despacho, en orden: las del seguimiento de la tienda.</summary>
    public static readonly IReadOnlyList<string> Etapas =
        StubOrderTrackingService.ShopPipeline.Select(e => e.Stage).ToList();

    private static readonly IReadOnlyDictionary<string, string> ActoDeLaEtapa = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["paid"] = "preparar",
        ["preparing"] = "enviar",
        ["shipped"] = "entregar",
    };

    private readonly OrdenesDeLaTienda _ordenes;
    private readonly IOrderTrackingService _seguimiento;
    private readonly IPuertaDeLosFlujos _puerta;

    public PedidosDeLaTienda(OrdenesDeLaTienda ordenes, IOrderTrackingService seguimiento, IPuertaDeLosFlujos puerta)
    {
        _ordenes = ordenes;
        _seguimiento = seguimiento;
        _puerta = puerta;
    }

    /// <summary>Si <paramref name="etapa"/> es una etapa del despacho.</summary>
    public static bool EsEtapa(string? etapa) => etapa is not null && Etapas.Contains(etapa, StringComparer.Ordinal);

    /// <summary>La página <paramref name="pagina"/> (desde 1) de los pedidos en <paramref name="etapa"/>, o de todos.</summary>
    /// <exception cref="ArgumentException">Si la etapa no es del despacho.</exception>
    public async Task<BandejaDeLaTienda> BandejaAsync(string? etapa, int pagina, CancellationToken ct)
    {
        var filtro = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();
        if (filtro is not null && !EsEtapa(filtro))
        {
            throw new ArgumentException($"«{etapa}» no es una etapa del despacho.", nameof(etapa));
        }
        pagina = Math.Max(1, pagina);

        var pedidos = await PagadosAsync(ct).ConfigureAwait(false);
        var cuentas = Etapas.ToDictionary(e => e, e => pedidos.Count(p => p.Etapa == e), StringComparer.Ordinal);
        var filtrados = filtro is null ? pedidos : pedidos.Where(p => p.Etapa == filtro).ToList();

        return new BandejaDeLaTienda(
            filtrados.Skip((pagina - 1) * PorPagina).Take(PorPagina).ToList(), pagina, PorPagina, filtrados.Count, cuentas);
    }

    /// <summary>Un pedido pagado de la tienda, o nulo si no existe o no está pagado.</summary>
    public async Task<PedidoDeLaTienda?> PedidoAsync(string? orderRef, CancellationToken ct)
    {
        var orden = await _ordenes.UnaAsync(orderRef, ct).ConfigureAwait(false);
        return orden is { Status: OrderStatus.Paid } ? await ComoPedidoAsync(orden, ct).ConfigureAwait(false) : null;
    }

    private async Task<List<PedidoDeLaTienda>> PagadosAsync(CancellationToken ct)
    {
        var pedidos = new List<PedidoDeLaTienda>();
        foreach (var orden in await _ordenes.TodasAsync(ct).ConfigureAwait(false))
        {
            if (orden.Status == OrderStatus.Paid) pedidos.Add(await ComoPedidoAsync(orden, ct).ConfigureAwait(false));
        }
        return pedidos;
    }

    private async Task<PedidoDeLaTienda> ComoPedidoAsync(ShopOrder orden, CancellationToken ct)
    {
        var etapa = (await _seguimiento.GetTimelineAsync(orden.OrderRef, ct).ConfigureAwait(false))?.CurrentStage;
        etapa = EsEtapa(etapa) ? etapa! : StubOrderTrackingService.StagePaid;

        var (actos, motivo) = ActosDe(orden, etapa);
        return new PedidoDeLaTienda(orden, orden.ViaGate ? "puerta" : "directa", etapa, actos, motivo);
    }

    private (IReadOnlyList<string> Actos, string? Motivo) ActosDe(ShopOrder orden, string etapa)
    {
        if (!ActoDeLaEtapa.TryGetValue(etapa, out var acto)) return ([], "entregado");
        if (orden.ViaGate && !(_puerta.Abre(FlujoDelEnvio) && _puerta.Abre(FlujoDeLaEntrega)))
        {
            return ([], "despacho_no_disponible");
        }
        return ([acto], null);
    }
}
