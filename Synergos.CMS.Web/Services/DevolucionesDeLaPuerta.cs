using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Services;

/// <summary>Lo que devolvió el orquestador de una compra de la puerta, o por qué no devolvió nada.</summary>
/// <param name="Devuelto">Lo que el flujo dice haber devuelto; cero si no salió.</param>
/// <param name="Codigo">El código del rechazo (el de la capacidad, el del flujo o el de la puerta), o nulo si salió.</param>
/// <param name="Mensaje">El motivo, para quien lleva la devolución.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto.</param>
public sealed record DevolucionDeLaPuerta(decimal Devuelto, string? Codigo = null, string? Mensaje = null, bool Transitorio = false)
{
    /// <summary>Si la plata salió.</summary>
    public bool Bien => Codigo is null;
}

/// <summary>
/// Devolver parte de una compra que salió por la PUERTA (004-tienda-compra, US 7, T040/T041): el flujo posterior
/// <c>tienda.devolucion</c> del orquestador, del personal, pedido por el reenvío de la puerta —sin cliente propio
/// del vertical— cuando una devolución llega a «reembolsada».
/// </summary>
/// <remarks>
/// <para><b>Los mismos controles que la puerta le hace a un pedido del navegador</b> (núcleo, P8 = A del lote 6): el
/// flujo tiene que estar abierto en este sitio y quien devuelve, ser un miembro con uno de los roles del flujo; el
/// sujeto es esa persona, con el <c>SujetoKind</c> del flujo, y queda como dueña de su devolución —el rastro de quién
/// la hizo—.</para>
///
/// <para><b>La llave es la de la DEVOLUCIÓN, no la de la persona.</b> Si un corte de red deja la respuesta en el aire y
/// otra persona del personal reintenta la misma devolución, la misma llave lleva a la misma saga y la plata no sale
/// dos veces; una llave por persona la devolvería otra vez.</para>
///
/// <para><b>Lo que se devuelve es lo que dice el flujo</b> (<c>datos.monto</c> de una saga completada), y un rechazo
/// pasa con su código —<c>payments.refund_over_captured</c>, <c>flow.original_no_completada</c>…— para que la
/// devolución se quede donde estaba con el motivo puesto.</para>
/// </remarks>
public sealed class DevolucionesDeLaPuerta
{
    /// <summary>El flujo posterior de la tienda.</summary>
    public const string Flujo = "tienda.devolucion";

    /// <summary>La operación que lo abre: el nombre de su única fase.</summary>
    public const string Operacion = "devolver";

    private const string Origen = "rma";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TablaDeLaPuerta _tabla;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ReenvioDeLaPuerta _reenvio;
    private readonly IMemberAccessGate _miembro;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;

    public DevolucionesDeLaPuerta(
        TablaDeLaPuerta tabla, IPuertaDeLosFlujos puerta, ReenvioDeLaPuerta reenvio, IMemberAccessGate miembro,
        IOptionsMonitor<PuertaSettings> ajustes)
    {
        _tabla = tabla;
        _puerta = puerta;
        _reenvio = reenvio;
        _miembro = miembro;
        _ajustes = ajustes;
    }

    /// <summary>La llave de una devolución: de la compra y de su motivo (la RMA), nunca de quien la pide.</summary>
    public static string Llave(string compra, string motivo) => LoQuePoneLaPuerta.LlaveReemitida(Origen, Flujo, $"{compra}|{motivo}");

    /// <summary>Devuelve <paramref name="monto"/> de la compra <paramref name="compra"/> por el orquestador.</summary>
    /// <param name="motivo">El de la devolución (<c>RMA …</c>): con la compra, es la llave.</param>
    public async Task<DevolucionDeLaPuerta> DevolverAsync(string compra, decimal monto, string moneda, string motivo, CancellationToken ct)
    {
        var op = _tabla.Buscar(Flujo, Operacion);
        if (op is null || !_puerta.Abre(Flujo) || !_ajustes.CurrentValue.Flujos.TryGetValue(Flujo, out var flujo)
            || string.IsNullOrWhiteSpace(flujo.SujetoKind))
        {
            return new(0m, "tienda.devolucion_no_disponible",
                "La devolución de las compras de la puerta no está abierta en este sitio: la plata sigue en el orquestador.");
        }

        if (!_miembro.IsAuthenticated || _miembro.CurrentMemberKey is not { } quien)
        {
            return new(0m, "puerta.sesion_requerida", "Devolver la plata de una compra pide iniciar sesión como personal de la tienda.");
        }
        var roles = string.Join(',', flujo.Roles.Where(r => !string.IsNullOrWhiteSpace(r)));
        if (roles.Length == 0 || !_miembro.HasAnyRole(roles))
        {
            return new(0m, "puerta.personal_requerido", "Devolver la plata de una compra es del personal de la tienda.");
        }

        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(new { compra, monto = new { amount = monto, currency = moneda } });
        var cabeceras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LoQuePoneLaPuerta.CabeceraDelSujeto] = LoQuePoneLaPuerta.Sujeto(flujo.SujetoKind, quien),
            [LoQuePoneLaPuerta.CabeceraDeLaLlave] = Llave(compra, motivo),
        };
        var salida = await _reenvio.EnviarAsync(op, new Dictionary<string, string>(StringComparer.Ordinal), cuerpo, cabeceras, ct)
            .ConfigureAwait(false);

        if (salida.Fallo is { } fallo) return new(0m, fallo.Codigo, fallo.Mensaje, fallo.Transitorio);
        return salida.Estado is >= 200 and < 300 ? Hecha(salida.Cuerpo) : await RechazadaAsync(salida.Cuerpo, ct).ConfigureAwait(false);
    }

    private static DevolucionDeLaPuerta Hecha(byte[] cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        var raiz = doc.RootElement;
        if (raiz.TryGetProperty("estado", out var estado) && estado.GetString() == "Completed"
            && raiz.TryGetProperty("datos", out var datos) && datos.TryGetProperty("monto", out var devuelto)
            && devuelto.TryGetProperty("amount", out var cuanto) && cuanto.TryGetDecimal(out var valor))
        {
            return new(valor);
        }

        // Abierta y no terminada: la devolución sigue en curso (una compensación, un turno). No se afirma nada.
        return new(0m, "tienda.devolucion_en_curso", "La devolución todavía no terminó en el orquestador: reintentar con la misma devolución no devuelve dos veces.", Transitorio: true);
    }

    /// <summary>El rechazo del orquestador, leído por la única pieza que lee rechazos del árbol (#129).</summary>
    private static async Task<DevolucionDeLaPuerta> RechazadaAsync(byte[] cuerpo, CancellationToken ct)
    {
        using var respuesta = new HttpResponseMessage { Content = new ByteArrayContent(cuerpo) };
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(respuesta, Json, ct).ConfigureAwait(false);
        return new(0m, rechazo?.Codigo ?? "tienda.respuesta_invalida", rechazo?.Detalle ?? "El orquestador no pudo devolver la plata.",
            rechazo?.EsTransitorio == true);
    }
}
