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

/// <summary>A dónde va el envío de una compra de la puerta: lo que el comprador dio al cerrar, y que sólo guarda el envío.</summary>
public sealed record DestinoDelEnvio(
    string? Line1, string? Line2, string? City, string? Region, string? PostalCode, string? Country, string? Contact);

/// <summary>Un paso del envío: su estado, su nota (la guía) y cuándo pasó.</summary>
public sealed record EventoDelEnvio(string Estado, string? Nota, DateTimeOffset Fecha);

/// <summary>El envío de una compra de la puerta, como lo lee el personal para despacharla (013-seller-operaciones, T010).</summary>
/// <param name="Estado">El de la capacidad: <c>Ready</c>, <c>InTransit</c>, <c>Delivered</c>, <c>Failed</c> o <c>Returned</c>.</param>
public sealed record EnvioDeLaTienda(DestinoDelEnvio? Destino, string? Transportadora, string Estado, IReadOnlyList<EventoDelEnvio> Eventos);

/// <summary>
/// Despachar una compra que salió por la PUERTA (013-seller-operaciones, US 2, T018): los flujos posteriores
/// <c>tienda.envio</c> y <c>tienda.entrega</c> del orquestador, del personal, pedidos por el reenvío de la puerta —sin
/// cliente propio del vertical— cuando alguien del personal da el acto en la consola; y la lectura <c>detalle</c> de
/// <c>tienda.envio</c>, que cuenta el envío de la compra (NS-2 de 013).
/// </summary>
/// <remarks>
/// <para><b>Los mismos controles que la puerta le hace a un pedido del navegador</b> (núcleo, P8 = A del lote 6), con el
/// molde de <see cref="DevolucionesDeLaPuerta"/>: el flujo abierto en este sitio, y quien despacha, un miembro con uno de
/// los roles del flujo. En un acto, el sujeto es esa persona, con el <c>SujetoKind</c> del flujo; la lectura no lleva
/// sujeto ni llave, porque no abre saga.</para>
///
/// <para><b>La llave es la del ACTO sobre la compra, no la de la persona</b>: <c>{compra}|enviar</c> o
/// <c>{compra}|entregar</c>. El reintento de una respuesta perdida vuelve a la misma saga. Otra persona con la misma llave
/// abre la suya, porque en el orquestador la llave es de su dueño (ADR 0140 F3), y es inocuo: el paso cuenta como hecho
/// lo que ya estaba, sin otro evento en el envío.</para>
///
/// <para><b>Lo que se lee es lo que dice el flujo</b> (<c>datos.estadoDelEnvio</c> de una saga completada, o los
/// <c>datos</c> de la lectura), y un rechazo pasa con su código —<c>fulfillment.transition_not_allowed</c>,
/// <c>flow.original_no_completada</c>…— para que el seguimiento del comprador se quede donde estaba.</para>
/// </remarks>
public sealed class DespachosDeLaPuerta
{
    /// <summary>El flujo que marca enviado el pedido.</summary>
    public const string FlujoDelEnvio = "tienda.envio";

    /// <summary>El flujo que lo marca entregado.</summary>
    public const string FlujoDeLaEntrega = "tienda.entrega";

    /// <summary>La operación que los abre: el nombre de su única fase.</summary>
    public const string Operacion = "abrir";

    /// <summary>La lectura del personal que cuenta el envío de una compra: la declara <see cref="FlujoDelEnvio"/>.</summary>
    public const string Lectura = "detalle";

    /// <summary>Un despacho que este sitio no abre: sin el orquestador de la tienda o sin su entrada en la puerta.</summary>
    public const string NoDisponible = "tienda.despacho_no_disponible";

    /// <summary>Lo que contesta el orquestador por una compra que no existe: no hay envío que leer.</summary>
    public const string CompraNoEncontrada = "flow.original_no_encontrada";

    /// <summary>Lo que contesta por una compra que no se completó (en curso o deshecha): todavía no hay envío.</summary>
    public const string CompraNoCompletada = "flow.original_no_completada";

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

    /// <summary>
    /// El envío de la compra <paramref name="compra"/>: a dónde va, con quién, en qué estado y por dónde ha ido. Lo cuenta
    /// la lectura <see cref="Lectura"/> de <see cref="FlujoDelEnvio"/>, sin saga.
    /// </summary>
    /// <returns>El envío, o el motivo: <see cref="CompraNoEncontrada"/> o <see cref="CompraNoCompletada"/> si no hay envío
    /// que leer; transitorio si el orquestador o la capacidad de envíos no contestan.</returns>
    public async Task<(EnvioDeLaTienda? Envio, DespachoDeLaPuerta? Fallo)> DetalleAsync(string compra, CancellationToken ct)
    {
        var (op, _, _, cerrada) = Guarda(FlujoDelEnvio, Lectura, conSujeto: false);
        if (cerrada is not null) return (null, cerrada);

        var salida = await _reenvio.EnviarAsync(
                op!, new Dictionary<string, string>(StringComparer.Ordinal), JsonSerializer.SerializeToUtf8Bytes(new { compra }),
                new Dictionary<string, string>(StringComparer.Ordinal), ct)
            .ConfigureAwait(false);

        if (salida.Fallo is { } fallo) return (null, new(null, fallo.Codigo, fallo.Mensaje, fallo.Transitorio));
        if (salida.Estado is < 200 or >= 300)
        {
            return (null, await RechazadoAsync(salida.Cuerpo, "El orquestador no pudo leer el envío.", ct).ConfigureAwait(false));
        }
        return Leido(salida.Cuerpo) is { } envio
            ? (envio, null)
            : (null, new(null, "tienda.respuesta_invalida", "El orquestador no dijo cómo va el envío."));
    }

    private async Task<DespachoDeLaPuerta> DespacharAsync(string flujo, string acto, string compra, object cuerpo, CancellationToken ct)
    {
        var (op, ajustes, quien, cerrada) = Guarda(flujo, Operacion, conSujeto: true);
        if (cerrada is not null) return cerrada;

        var cabeceras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LoQuePoneLaPuerta.CabeceraDelSujeto] = LoQuePoneLaPuerta.Sujeto(ajustes!.SujetoKind!, quien),
            [LoQuePoneLaPuerta.CabeceraDeLaLlave] = Llave(flujo, compra, acto),
        };
        var salida = await _reenvio.EnviarAsync(
                op!, new Dictionary<string, string>(StringComparer.Ordinal), JsonSerializer.SerializeToUtf8Bytes(cuerpo), cabeceras, ct)
            .ConfigureAwait(false);

        if (salida.Fallo is { } fallo) return new(null, fallo.Codigo, fallo.Mensaje, fallo.Transitorio);
        return salida.Estado is >= 200 and < 300
            ? Hecho(salida.Cuerpo)
            : await RechazadoAsync(salida.Cuerpo, "El orquestador no pudo mover el envío.", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lo que la puerta comprueba antes de mandar: la operación expuesta y el flujo abierto en este sitio —con su
    /// <c>SujetoKind</c>, si lleva sujeto—, una sesión, y uno de los roles del flujo.
    /// </summary>
    private (OperacionDeLaPuerta? Op, FlujoDeLaPuertaSettings? Ajustes, Guid Quien, DespachoDeLaPuerta? Fallo) Guarda(
        string flujo, string operacion, bool conSujeto)
    {
        var op = _tabla.Buscar(flujo, operacion);
        if (op is null || !_puerta.Abre(flujo) || !_ajustes.CurrentValue.Flujos.TryGetValue(flujo, out var ajustes)
            || (conSujeto && string.IsNullOrWhiteSpace(ajustes.SujetoKind)))
        {
            return (null, null, default,
                new(null, NoDisponible, "El despacho de las compras de la puerta no está abierto en este sitio: el envío sigue donde estaba."));
        }

        if (!_miembro.IsAuthenticated || _miembro.CurrentMemberKey is not { } quien)
        {
            return (null, null, default, new(null, "puerta.sesion_requerida", "Despachar un pedido pide iniciar sesión como personal de la tienda."));
        }
        var roles = string.Join(',', ajustes.Roles.Where(r => !string.IsNullOrWhiteSpace(r)));
        if (roles.Length == 0 || !_miembro.HasAnyRole(roles))
        {
            return (null, null, default, new(null, "puerta.personal_requerido", "Despachar un pedido es del personal de la tienda."));
        }
        return (op, ajustes, quien, null);
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

    /// <summary>Los <c>datos</c> de la lectura, o nada si no traen el estado del envío.</summary>
    private static EnvioDeLaTienda? Leido(byte[] cuerpo)
    {
        try
        {
            var datos = JsonSerializer.Deserialize<DetalleLeido>(cuerpo, Json)?.Datos;
            if (datos is not { EstadoDelEnvio: { Length: > 0 } estado }) return null;
            var destino = datos.Destino is { } d
                ? new DestinoDelEnvio(d.Linea1, d.Linea2, d.Ciudad, d.Region, d.CodigoPostal, d.Pais, d.Contacto)
                : null;
            return new EnvioDeLaTienda(destino, datos.Transportadora, estado,
                (datos.Eventos ?? []).Select(e => new EventoDelEnvio(e.Estado, e.Nota, e.Instante)).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>El rechazo del orquestador, leído por la única pieza que lee rechazos del árbol (#129).</summary>
    private static async Task<DespachoDeLaPuerta> RechazadoAsync(byte[] cuerpo, string porDefecto, CancellationToken ct)
    {
        using var respuesta = new HttpResponseMessage { Content = new ByteArrayContent(cuerpo) };
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(respuesta, Json, ct).ConfigureAwait(false);
        return new(null, rechazo?.Codigo ?? "tienda.respuesta_invalida", rechazo?.Detalle ?? porDefecto, rechazo?.EsTransitorio == true);
    }

    /// <summary>Lo que el despacho lee de la lectura <c>detalle</c> (<c>LecturaDeFlujo</c> en <c>Synergos.Bff.Tienda.json</c>).</summary>
    internal sealed record DetalleLeido(DatosDelDetalle? Datos);

    internal sealed record DatosDelDetalle(DestinoLeido? Destino, string? Transportadora, string? EstadoDelEnvio, IReadOnlyList<EventoLeido>? Eventos);

    internal sealed record DestinoLeido(
        string? Linea1, string? Linea2, string? Ciudad, string? Region, string? CodigoPostal, string? Pais, string? Contacto);

    internal sealed record EventoLeido(string Estado, string? Nota, DateTimeOffset Instante);
}
