using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Services.Gobierno;

/// <summary>Cómo terminó un acto sobre un expediente de la puerta: hecho, o el rechazo con su código.</summary>
/// <param name="Estado">El estado HTTP con que se contesta a quien pidió el acto.</param>
/// <param name="Codigo">El código del rechazo (el de la capacidad, el del flujo o el de la puerta), o nulo si salió.</param>
/// <param name="Mensaje">El motivo, para el registro.</param>
/// <param name="Transitorio">Si reintentar con la misma llave puede salir distinto.</param>
public sealed record ActoDeLaPuerta(int Estado, string? Codigo = null, string? Mensaje = null, bool Transitorio = false)
{
    /// <summary>Si el acto quedó hecho en el proceso.</summary>
    public bool Bien => Codigo is null;
}

/// <summary>
/// Los actos sobre un expediente nacido por la puerta (009-gobierno-tramite T039): tomar, decidir y subsanar son los
/// flujos posteriores <c>gobierno.toma</c>, <c>gobierno.decision</c> y <c>gobierno.subsanacion</c> del orquestador,
/// pedidos por el reenvío de la puerta —sin cliente propio del vertical—, el molde de <see cref="DevolucionesDeLaPuerta"/>.
/// </summary>
/// <remarks>
/// <para><b>Por qué los pide el CMS y no el elemento.</b> El coordinador del elemento atiende un solo flujo, así que el
/// elemento necesitaría los cuatro encadenados alrededor, y la regla de las vistas (NR-10) sólo deja anidar un flujo
/// dentro de su original: la decisión no es posterior a la toma. La consola sigue pidiendo cada acto a su ruta del CMS,
/// y el CMS decide el camino por el expediente: la ruta directa, o la puerta.</para>
///
/// <para><b>Los mismos controles que la puerta le hace a un pedido del navegador</b> (núcleo, P8 = A del lote 6): el
/// flujo tiene que estar abierto en el sitio y, en uno del personal, quien actúa tiene uno de sus roles; el sujeto es esa
/// persona con el <c>SujetoKind</c> del flujo: la historia del proceso la nombra a ella.</para>
///
/// <para><b>La llave es la de la intención que manda la consola, atada a la persona</b>, como la reemite la puerta: el
/// doble clic de una persona cae en el mismo acto, y dos funcionarios a la vez son dos actos —uno cambia el expediente y
/// el otro recibe <c>workflow.transition_not_allowed</c>—.</para>
/// </remarks>
public sealed class ActosDeLaPuerta
{
    /// <summary>Tomar el expediente: del personal.</summary>
    public const string Toma = "gobierno.toma";

    /// <summary>Aprobar, rechazar o pedir información: del personal.</summary>
    public const string Decision = "gobierno.decision";

    /// <summary>Subsanar: del ciudadano dueño, como miembro.</summary>
    public const string Subsanacion = "gobierno.subsanacion";

    /// <summary>La operación que abre cada acto: el nombre de su única fase.</summary>
    public const string Operacion = "abrir";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TablaDeLaPuerta _tabla;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ReenvioDeLaPuerta _reenvio;
    private readonly IMemberAccessGate _miembro;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;

    public ActosDeLaPuerta(
        TablaDeLaPuerta tabla, IPuertaDeLosFlujos puerta, ReenvioDeLaPuerta reenvio, IMemberAccessGate miembro,
        IOptionsMonitor<PuertaSettings> ajustes)
    {
        _tabla = tabla;
        _puerta = puerta;
        _reenvio = reenvio;
        _miembro = miembro;
        _ajustes = ajustes;
    }

    public Task<ActoDeLaPuerta> TomarAsync(string radicacion, string? nota, string llave, CancellationToken ct)
        => ActuarAsync(Toma, new { radicacion, nota }, llave, ct);

    public Task<ActoDeLaPuerta> DecidirAsync(string radicacion, string decision, string nota, string llave, CancellationToken ct)
        => ActuarAsync(Decision, new { radicacion, decision, nota }, llave, ct);

    public Task<ActoDeLaPuerta> SubsanarAsync(string radicacion, string? nota, string llave, CancellationToken ct)
        => ActuarAsync(Subsanacion, new { radicacion, nota }, llave, ct);

    private async Task<ActoDeLaPuerta> ActuarAsync(string flujo, object cuerpo, string llave, CancellationToken ct)
    {
        var op = _tabla.Buscar(flujo, Operacion);
        if (op is null || !_puerta.Abre(flujo) || !_ajustes.CurrentValue.Flujos.TryGetValue(flujo, out var config)
            || string.IsNullOrWhiteSpace(config.SujetoKind))
        {
            return new(StatusCodes.Status503ServiceUnavailable, "puerta.flujo_no_disponible",
                $"«{flujo}» no está abierto en este sitio: el expediente de la puerta no se puede mover ahora.", Transitorio: true);
        }
        if (!_miembro.IsAuthenticated || _miembro.CurrentMemberKey is not { } quien)
        {
            return new(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida", "Hace falta iniciar sesión.");
        }
        if (string.Equals(config.Acceso, AccesoDeLaPuerta.Personal, StringComparison.Ordinal))
        {
            var roles = string.Join(',', config.Roles.Where(r => !string.IsNullOrWhiteSpace(r)));
            if (roles.Length == 0 || !_miembro.HasAnyRole(roles))
            {
                return new(StatusCodes.Status403Forbidden, "puerta.personal_requerido", "El acto es del personal de la entidad.");
            }
        }

        var sujeto = LoQuePoneLaPuerta.Sujeto(config.SujetoKind, quien);
        var cabeceras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LoQuePoneLaPuerta.CabeceraDelSujeto] = sujeto,
            [LoQuePoneLaPuerta.CabeceraDeLaLlave] = LoQuePoneLaPuerta.LlaveReemitida(sujeto, flujo, llave),
        };
        var salida = await _reenvio.EnviarAsync(
                op, new Dictionary<string, string>(StringComparer.Ordinal), JsonSerializer.SerializeToUtf8Bytes(cuerpo, Json), cabeceras, ct)
            .ConfigureAwait(false);

        if (salida.Fallo is { } fallo) return new(fallo.Estado, fallo.Codigo, fallo.Mensaje, fallo.Transitorio);
        if (salida.Estado is >= 200 and < 300) return Hecho(salida.Cuerpo);

        using var respuesta = new HttpResponseMessage { Content = new ByteArrayContent(salida.Cuerpo) };
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(respuesta, Json, ct).ConfigureAwait(false);
        return new(salida.Estado, rechazo?.Codigo ?? "gov.respuesta_invalida", rechazo?.Detalle ?? "El orquestador no pudo hacer el acto.",
            rechazo?.EsTransitorio == true);
    }

    /// <summary>Completado es hecho; abierto y sin terminar no se afirma: reintentar con la misma llave lo retoma.</summary>
    private static ActoDeLaPuerta Hecho(byte[] cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        return doc.RootElement.TryGetProperty("estado", out var estado) && estado.GetString() == "Completed"
            ? new(StatusCodes.Status200OK)
            : new(StatusCodes.Status503ServiceUnavailable, "gov.acto_en_curso",
                "El acto todavía no terminó en el orquestador: reintentarlo con la misma llave no lo repite.", Transitorio: true);
    }
}
