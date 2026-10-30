using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Services;

/// <summary>Lo que dejó el despacho en el orquestador: el estado en que quedó el envío, o por qué no se movió.</summary>
/// <param name="EstadoDelEnvio">El que escribió el flujo (<c>InTransit</c>, <c>Delivered</c>); nulo si no salió.</param>
/// <param name="Codigo">El código del rechazo (el de la capacidad, el del flujo o el de la puerta), o nulo si salió.</param>
/// <param name="Mensaje">El motivo, para quien despacha.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto.</param>
public sealed record DespachoDeLaPuerta(string? EstadoDelEnvio, string? Codigo = null, string? Mensaje = null, bool Transitorio = false)
{
    /// <summary>Si el envío quedó donde se pidió (o ya estaba).</summary>
    public bool Bien => Codigo is null;
}

/// <summary>
/// Despachar una compra que salió por la PUERTA (013-seller-operaciones, US 2, T018): los flujos posteriores
/// <c>tienda.envio</c> y <c>tienda.entrega</c> del orquestador, del personal, pedidos por el reenvío de la puerta —sin
/// cliente propio del vertical— cuando alguien del personal da el acto en la consola.
/// </summary>
/// <remarks>
/// <para><b>Los mismos controles que la puerta le hace a un pedido del navegador</b> (núcleo, P8 = A del lote 6), con el
/// molde de <see cref="DevolucionesDeLaPuerta"/>: el flujo abierto en este sitio, y quien despacha, un miembro con uno de
/// los roles del flujo. El sujeto es esa persona, con el <c>SujetoKind</c> del flujo.</para>
///
/// <para><b>La llave es la del ACTO sobre la compra, no la de la persona</b>: <c>{compra}|enviar</c> o
/// <c>{compra}|entregar</c>. El reintento de una respuesta perdida vuelve a la misma saga. Otra persona con la misma llave
/// abre la suya, porque en el orquestador la llave es de su dueño (ADR 0140 F3), y es inocuo: el paso cuenta como hecho
/// lo que ya estaba, sin otro evento en el envío.</para>
///
/// <para><b>Lo que se lee es lo que dice el flujo</b> (<c>datos.estadoDelEnvio</c> de una saga completada), y un rechazo
/// pasa con su código —<c>fulfillment.transition_not_allowed</c>, <c>flow.original_no_completada</c>…— para que el
/// seguimiento del comprador se quede donde estaba.</para>
/// </remarks>
public sealed class DespachosDeLaPuerta
{
    /// <summary>El flujo que marca enviado el pedido.</summary>
    public const string FlujoDelEnvio = "tienda.envio";

    /// <summary>El flujo que lo marca entregado.</summary>
    public const string FlujoDeLaEntrega = "tienda.entrega";

    /// <summary>La operación que los abre: el nombre de su única fase.</summary>
    public const string Operacion = "abrir";

    /// <summary>Un despacho que este sitio no abre: sin el orquestador de la tienda o sin su entrada en la puerta.</summary>
    public const string NoDisponible = "tienda.despacho_no_disponible";

    private const string Origen = "despacho";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TablaDeLaPuerta _tabla;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ReenvioDeLaPuerta _reenvio;
    private readonly IMemberAccessGate _miembro;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;

    public DespachosDeLaPuerta(
        TablaDeLaPuerta tabla, IPuertaDeLosFlujos puerta, ReenvioDeLaPuerta reenvio, IMemberAccessGate miembro,
        IOptionsMonitor<PuertaSettings> ajustes)
    {
        _tabla = tabla;
        _puerta = puerta;
        _reenvio = reenvio;
        _miembro = miembro;
        _ajustes = ajustes;
    }

    /// <summary>La llave de un acto: del flujo, de la compra y del acto, nunca de quien lo da.</summary>
    public static string Llave(string flujo, string compra, string acto) => LoQuePoneLaPuerta.LlaveReemitida(Origen, flujo, $"{compra}|{acto}");

    /// <summary>Marca enviado el pedido de la compra <paramref name="compra"/>, con la guía como nota si la hay.</summary>
    public Task<DespachoDeLaPuerta> EnviarAsync(string compra, string? guia, CancellationToken ct)
        => DespacharAsync(FlujoDelEnvio, "enviar", compra, new { compra, guia = string.IsNullOrWhiteSpace(guia) ? null : guia.Trim() }, ct);

    /// <summary>Marca entregado el pedido de la compra <paramref name="compra"/>.</summary>
    public Task<DespachoDeLaPuerta> EntregarAsync(string compra, CancellationToken ct)
        => DespacharAsync(FlujoDeLaEntrega, "entregar", compra, new { compra }, ct);

    private async Task<DespachoDeLaPuerta> DespacharAsync(string flujo, string acto, string compra, object cuerpo, CancellationToken ct)
    {
        var op = _tabla.Buscar(flujo, Operacion);
        if (op is null || !_puerta.Abre(flujo) || !_ajustes.CurrentValue.Flujos.TryGetValue(flujo, out var ajustes)
            || string.IsNullOrWhiteSpace(ajustes.SujetoKind))
        {
            return new(null, NoDisponible, "El despacho de las compras de la puerta no está abierto en este sitio: el envío sigue donde estaba.");
        }

        if (!_miembro.IsAuthenticated || _miembro.CurrentMemberKey is not { } quien)
        {
            return new(null, "puerta.sesion_requerida", "Despachar un pedido pide iniciar sesión como personal de la tienda.");
        }
        var roles = string.Join(',', ajustes.Roles.Where(r => !string.IsNullOrWhiteSpace(r)));
        if (roles.Length == 0 || !_miembro.HasAnyRole(roles))
        {
            return new(null, "puerta.personal_requerido", "Despachar un pedido es del personal de la tienda.");
        }

        var cabeceras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LoQuePoneLaPuerta.CabeceraDelSujeto] = LoQuePoneLaPuerta.Sujeto(ajustes.SujetoKind, quien),
            [LoQuePoneLaPuerta.CabeceraDeLaLlave] = Llave(flujo, compra, acto),
        };
        var salida = await _reenvio.EnviarAsync(
                op, new Dictionary<string, string>(StringComparer.Ordinal), JsonSerializer.SerializeToUtf8Bytes(cuerpo), cabeceras, ct)
            .ConfigureAwait(false);

        if (salida.Fallo is { } fallo) return new(null, fallo.Codigo, fallo.Mensaje, fallo.Transitorio);
        return salida.Estado is >= 200 and < 300 ? Hecho(salida.Cuerpo) : await RechazadoAsync(salida.Cuerpo, ct).ConfigureAwait(false);
    }

    private static DespachoDeLaPuerta Hecho(byte[] cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        var raiz = doc.RootElement;
        if (raiz.TryGetProperty("estado", out var estado) && estado.GetString() == "Completed"
            && raiz.TryGetProperty("datos", out var datos) && datos.TryGetProperty("estadoDelEnvio", out var envio)
            && envio.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(envio.GetString()))
        {
            return new(envio.GetString());
        }

        // Abierta y no terminada (un transitorio a medias): no se afirma nada, y repetir el mismo acto la retoma.
        return new(null, "tienda.despacho_en_curso",
            "El despacho todavía no terminó en el orquestador: repetir el mismo acto no mueve el envío dos veces.", Transitorio: true);
    }

    /// <summary>El rechazo del orquestador, leído por la única pieza que lee rechazos del árbol (#129).</summary>
    private static async Task<DespachoDeLaPuerta> RechazadoAsync(byte[] cuerpo, CancellationToken ct)
    {
        using var respuesta = new HttpResponseMessage { Content = new ByteArrayContent(cuerpo) };
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(respuesta, Json, ct).ConfigureAwait(false);
        return new(null, rechazo?.Codigo ?? "tienda.respuesta_invalida", rechazo?.Detalle ?? "El orquestador no pudo mover el envío.",
            rechazo?.EsTransitorio == true);
    }
}
