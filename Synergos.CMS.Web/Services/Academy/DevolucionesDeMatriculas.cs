using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Services.Academy;

/// <summary>Lo que devolvió el orquestador de una matrícula de la puerta, o por qué no devolvió nada.</summary>
/// <param name="Devuelto">Lo que el flujo dice haber devuelto; cero si no salió.</param>
/// <param name="Moneda">La moneda de lo devuelto, o vacía si no salió.</param>
/// <param name="Codigo">El código del rechazo (el de la capacidad, el del flujo o el de la puerta), o nulo si salió.</param>
/// <param name="Mensaje">El motivo, para quien lleva la devolución.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto.</param>
public sealed record DevolucionDeLaMatricula(decimal Devuelto, string Moneda, string? Codigo = null, string? Mensaje = null, bool Transitorio = false)
{
    /// <summary>Si la plata salió.</summary>
    public bool Bien => Codigo is null;
}

/// <summary>
/// Devolver una matrícula que salió por la PUERTA (lote 8, 010-P2 B, T036): el flujo posterior
/// <c>educacion.devolucion</c> del orquestador, del personal, pedido por el reenvío de la puerta —sin cliente propio del
/// vertical—, y al completarse, la baja en el libro que lee el aula. El molde de <see cref="DevolucionesDeLaPuerta"/>.
/// </summary>
/// <remarks>
/// <para><b>Los mismos controles que la puerta le hace a un pedido del navegador</b>: el flujo tiene que estar abierto en
/// este sitio y quien devuelve, ser un miembro con uno de los roles del flujo; el sujeto es esa persona, con el
/// <c>SujetoKind</c> del flujo, y queda como dueña de su devolución —el rastro de quién la hizo—.</para>
///
/// <para><b>La llave es la de la MATRÍCULA, no la de la persona</b>: una matrícula se devuelve entera y una sola vez, así
/// que dos personas del personal que la devuelven a la vez, o un reintento tras un corte, llegan a la misma saga y la
/// plata no sale dos veces.</para>
///
/// <para><b>Lo devuelto es lo que dice el flujo</b> (<c>datos.total</c> de una saga completada): el total cobrado, que
/// el flujo lee de la matrícula; nadie lo teclea. Un rechazo pasa con su código —<c>payments.refund_over_captured</c>,
/// <c>flow.original_no_completada</c>…—, y la matrícula sigue activa.</para>
/// </remarks>
public sealed class DevolucionesDeMatriculas
{
    /// <summary>El flujo posterior de la academia.</summary>
    public const string Flujo = "educacion.devolucion";

    /// <summary>La operación que lo abre: el nombre de su única fase.</summary>
    public const string Operacion = "devolver";

    private const string Origen = "devolucion";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TablaDeLaPuerta _tabla;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ReenvioDeLaPuerta _reenvio;
    private readonly IMemberAccessGate _miembro;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;
    private readonly IMatriculasDeLaPuerta _libro;
    private readonly ILogger<DevolucionesDeMatriculas> _log;

    public DevolucionesDeMatriculas(
        TablaDeLaPuerta tabla, IPuertaDeLosFlujos puerta, ReenvioDeLaPuerta reenvio, IMemberAccessGate miembro,
        IOptionsMonitor<PuertaSettings> ajustes, IMatriculasDeLaPuerta libro, ILogger<DevolucionesDeMatriculas> log)
    {
        _tabla = tabla;
        _puerta = puerta;
        _reenvio = reenvio;
        _miembro = miembro;
        _ajustes = ajustes;
        _libro = libro;
        _log = log;
    }

    /// <summary>La llave de la devolución de una matrícula: de la matrícula, nunca de quien la pide.</summary>
    public static string Llave(string matricula) => LoQuePoneLaPuerta.LlaveReemitida(Origen, Flujo, matricula);

    /// <summary>Devuelve lo cobrado por la matrícula <paramref name="matricula"/> (el id de su saga) y la da de baja.</summary>
    public async Task<DevolucionDeLaMatricula> DevolverAsync(string matricula, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matricula) || ReenvioDeLaPuerta.EsSegmentoDePunto(matricula))
        {
            return new(0m, string.Empty, "academy.devolucion_invalida", "Falta la matrícula que se devuelve.");
        }

        var op = _tabla.Buscar(Flujo, Operacion);
        if (op is null || !_puerta.Abre(Flujo) || !_ajustes.CurrentValue.Flujos.TryGetValue(Flujo, out var flujo)
            || string.IsNullOrWhiteSpace(flujo.SujetoKind))
        {
            return new(0m, string.Empty, "academy.devolucion_no_disponible",
                "La devolución de las matrículas de la puerta no está abierta en este sitio: la plata sigue en el orquestador.");
        }

        if (!_miembro.IsAuthenticated || _miembro.CurrentMemberKey is not { } quien)
        {
            return new(0m, string.Empty, "puerta.sesion_requerida", "Devolver una matrícula pide iniciar sesión como administrador.");
        }
        var roles = string.Join(',', flujo.Roles.Where(r => !string.IsNullOrWhiteSpace(r)));
        if (roles.Length == 0 || !_miembro.HasAnyRole(roles))
        {
            return new(0m, string.Empty, "puerta.personal_requerido", "Devolver una matrícula es del administrador de la academia.");
        }

        var id = matricula.Trim();
        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(new { matricula = id });
        var cabeceras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LoQuePoneLaPuerta.CabeceraDelSujeto] = LoQuePoneLaPuerta.Sujeto(flujo.SujetoKind, quien),
            [LoQuePoneLaPuerta.CabeceraDeLaLlave] = Llave(id),
        };
        var salida = await _reenvio.EnviarAsync(op, new Dictionary<string, string>(StringComparer.Ordinal), cuerpo, cabeceras, ct)
            .ConfigureAwait(false);

        if (salida.Fallo is { } fallo) return new(0m, string.Empty, fallo.Codigo, fallo.Mensaje, fallo.Transitorio);
        if (salida.Estado is not (>= 200 and < 300)) return await RechazadaAsync(salida.Cuerpo, ct).ConfigureAwait(false);

        var hecha = Hecha(salida.Cuerpo);
        if (hecha.Bien && !await _libro.DarDeBajaAsync(id, ct).ConfigureAwait(false))
        {
            // La plata salió y el libro no tiene esa matrícula (nunca se anotó): no hay aula que cerrar.
            _log.LogWarning("Se devolvió la matrícula {Matricula} de la puerta y el libro no la tenía anotada.", id);
        }
        return hecha;
    }

    private static DevolucionDeLaMatricula Hecha(byte[] cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        var raiz = doc.RootElement;
        if (raiz.TryGetProperty("estado", out var estado) && estado.GetString() == "Completed"
            && raiz.TryGetProperty("datos", out var datos) && datos.TryGetProperty("total", out var total)
            && total.TryGetProperty("amount", out var cuanto) && cuanto.TryGetDecimal(out var valor))
        {
            var moneda = total.TryGetProperty("currency", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            return new(valor, moneda);
        }

        // Abierta y no terminada: la devolución sigue en curso. No se afirma nada ni se da de baja.
        return new(0m, string.Empty, "academy.devolucion_en_curso",
            "La devolución todavía no terminó en el orquestador: reintentarla no devuelve dos veces.", Transitorio: true);
    }

    /// <summary>El rechazo del orquestador, leído por la única pieza que lee rechazos del árbol (#129).</summary>
    private static async Task<DevolucionDeLaMatricula> RechazadaAsync(byte[] cuerpo, CancellationToken ct)
    {
        using var respuesta = new HttpResponseMessage { Content = new ByteArrayContent(cuerpo) };
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(respuesta, Json, ct).ConfigureAwait(false);
        return new(0m, string.Empty, rechazo?.Codigo ?? "academy.respuesta_invalida",
            rechazo?.Detalle ?? "El orquestador no pudo devolver la matrícula.", rechazo?.EsTransitorio == true);
    }
}
