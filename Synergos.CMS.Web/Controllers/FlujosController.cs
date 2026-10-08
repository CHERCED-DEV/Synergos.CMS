using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La puerta de los flujos: <c>GET|POST /api/flujos/{flujo}/{operacion}</c> (ADR 0140 F3). Abre, al
/// navegador y con la sesión del miembro, las operaciones que un orquestador marca para ella.
/// </summary>
/// <remarks>
/// <para><b>No sabe de ningún flujo ni de ningún vertical</b>, y hay gate (<c>PuertaGenericaTests</c>):
/// qué operaciones hay sale del contrato incrustado de cada orquestador (<see cref="TablaDeLaPuerta"/>),
/// y lo que el CMS pone de su parte, de <c>Synergos:Puerta:Flujos</c>. El negocio —qué se cobra, qué se
/// aparta, de quién es la compra— lo decide el orquestador; la puerta sólo dice quién pide.</para>
///
/// <para><b>El orden de las comprobaciones es el contrato</b>, y cada una tiene su código:</para>
/// <list type="number">
///   <item>404 <c>puerta.operacion_desconocida</c>: un flujo que no existe y una operación que el
///   orquestador no marcó dan EXACTAMENTE lo mismo, para no enseñar qué hay detrás.</item>
///   <item>405 <c>puerta.metodo_no_permitido</c>.</item>
///   <item>503 <c>puerta.flujo_no_disponible</c>: marcada, pero el despliegue no la abrió o no dice
///   dónde vive su orquestador.</item>
///   <item>401 <c>puerta.sesion_requerida</c>.</item>
///   <item>403 <c>puerta.origen_no_permitido</c>, 415 <c>puerta.tipo_no_soportado</c> y 413
///   <c>puerta.cuerpo_demasiado_grande</c> (<see cref="FiltroMismoOrigenJson"/>).</item>
///   <item>400 <c>puerta.llave_requerida</c> y 400 <c>puerta.parametro_requerido</c>.</item>
/// </list>
/// <para>Y después lo que contesta el orquestador (<see cref="ReenvioDeLaPuerta"/>): sus 2xx y sus
/// rechazos tal cual; 502 <c>puerta.llave_rechazada</c>, 502 <c>puerta.respuesta_invalida</c>, 503
/// <c>puerta.orquestador_no_disponible</c> y 504 <c>puerta.tiempo_agotado</c>.</para>
///
/// <para><b>Sin <c>[ApiController]</c></b>: el cuerpo pasa tal cual y no hay modelo que validar; un 400
/// automático de MVC no llevaría código. Y se apagan las páginas de estado: un error de la puerta no
/// puede salir convertido en la página HTML de error del sitio.</para>
/// </remarks>
[Route("api/flujos")]
public sealed class FlujosController : ControllerBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly TablaDeLaPuerta _tabla;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;
    private readonly DestinosDeLaPuerta _destinos;
    private readonly IMemberAccessGate _miembro;
    private readonly SeccionesDelSitio? _secciones;
    private readonly ReenvioDeLaPuerta _reenvio;
    private readonly Func<string?> _sitio;

    /// <param name="sitio">El nombre del sitio de la petición, para el aviso. Nulo: el host.</param>
    public FlujosController(
        TablaDeLaPuerta tabla,
        IOptionsMonitor<PuertaSettings> ajustes,
        DestinosDeLaPuerta destinos,
        IMemberAccessGate miembro,
        ReenvioDeLaPuerta reenvio,
        SeccionesDelSitio? secciones = null,
        SitioDeLaPeticion? sitio = null)
    {
        _tabla = tabla;
        _ajustes = ajustes;
        _destinos = destinos;
        _miembro = miembro;
        _reenvio = reenvio;
        _secciones = secciones;
        _sitio = () => sitio?.Resolver().SiteRoot?.Name;
    }

    [Route("{flujo}/{operacion}")]
    public async Task<IActionResult> Pasar(string flujo, string operacion, CancellationToken ct)
    {
        if (HttpContext.Features.Get<IStatusCodePagesFeature>() is { } paginas) paginas.Enabled = false;
        Response.Headers.CacheControl = "no-store";

        var op = _tabla.Buscar(flujo, operacion);
        if (op is null)
        {
            return Fallo(new FalloDeLaPuerta(StatusCodes.Status404NotFound, "puerta.operacion_desconocida",
                "La puerta no expone esa operación."));
        }
        if (!string.Equals(Request.Method, op.Metodo, StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers.Allow = op.Metodo;
            return Fallo(new FalloDeLaPuerta(StatusCodes.Status405MethodNotAllowed, "puerta.metodo_no_permitido",
                $"Esta operación se pide por {op.Metodo}."));
        }
        if (!_ajustes.CurrentValue.Flujos.TryGetValue(op.Flujo, out var ajustes) || !_destinos.Tiene(op.Orquestador))
        {
            return Fallo(new FalloDeLaPuerta(StatusCodes.Status503ServiceUnavailable, "puerta.flujo_no_disponible",
                "Este flujo no está abierto en este sitio."));
        }
        if (!_miembro.IsAuthenticated || _miembro.CurrentMemberKey is not { } miembro || miembro == Guid.Empty)
        {
            return Fallo(new FalloDeLaPuerta(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida",
                "Hace falta iniciar sesión."));
        }
        if (FiltroMismoOrigenJson.Origen(Request) is { } ajeno) return Fallo(ajeno);

        byte[]? cuerpo = null;
        if (op.ConCuerpo)
        {
            if (FiltroMismoOrigenJson.Tipo(Request) is { } tipo) return Fallo(tipo);
            (cuerpo, var grande) = await FiltroMismoOrigenJson.LeerCuerpoAsync(Request, ct);
            if (grande is not null) return Fallo(grande);
        }

        var sujeto = LoQuePoneLaPuerta.Sujeto(ajustes.SujetoKind ?? string.Empty, miembro);
        var cabeceras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (op.LargoDeLaLlave is not null)
        {
            var llave = Request.Headers[LoQuePoneLaPuerta.CabeceraDeLaLlave].ToString();
            if (string.IsNullOrWhiteSpace(llave) || llave.Length > LoQuePoneLaPuerta.LargoMaximoDeLaLlaveDelNavegador)
            {
                if (op.LlaveRequerida || !string.IsNullOrEmpty(llave))
                {
                    return Fallo(new FalloDeLaPuerta(StatusCodes.Status400BadRequest, "puerta.llave_requerida",
                        $"Hace falta {LoQuePoneLaPuerta.CabeceraDeLaLlave}, la misma en cada reintento "
                        + $"(hasta {LoQuePoneLaPuerta.LargoMaximoDeLaLlaveDelNavegador} caracteres)."));
                }
            }
            else
            {
                cabeceras[LoQuePoneLaPuerta.CabeceraDeLaLlave] = LoQuePoneLaPuerta.LlaveReemitida(sujeto, op.Flujo, llave);
            }
        }

        var parametros = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in op.Parametros)
        {
            var valor = Request.Query[p.Nombre].ToString();
            if (!string.IsNullOrEmpty(valor)) parametros[p.Nombre] = valor;
            else if (p.Requerido || p.EnLaRuta)
            {
                return Fallo(new FalloDeLaPuerta(StatusCodes.Status400BadRequest, "puerta.parametro_requerido",
                    $"Hace falta «{p.Nombre}» en la consulta."));
            }
        }

        if (op.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto)) cabeceras[LoQuePoneLaPuerta.CabeceraDelSujeto] = sujeto;
        if (op.Declara(LoQuePoneLaPuerta.CabeceraDelNegocio)
            && ajustes.Negocio is { Seccion: { } seccion } negocio
            && _secciones?.Actual(seccion) is { } actual)
        {
            cabeceras[LoQuePoneLaPuerta.CabeceraDelNegocio] = LoQuePoneLaPuerta.Negocio(actual, negocio.Campos);
        }
        if (op.Declara(LoQuePoneLaPuerta.CabeceraDelContacto) && _miembro.CurrentMemberEmail is { Length: > 0 } correo)
        {
            var origen = $"{Request.Scheme}://{Request.Host}";
            cabeceras[LoQuePoneLaPuerta.CabeceraDelContacto] = LoQuePoneLaPuerta.Contacto(
                correo, _miembro.CurrentMemberDisplayName,
                LoQuePoneLaPuerta.Enlace(origen, ajustes.Aviso?.Ruta, parametros),
                _sitio() ?? Request.Host.Host);
        }

        var salida = await _reenvio.EnviarAsync(op, parametros, cuerpo, cabeceras, ct);
        if (salida.Fallo is not null) return Fallo(salida.Fallo);

        Response.StatusCode = salida.Estado;
        if (salida.TipoDeContenido is not null) Response.ContentType = salida.TipoDeContenido;
        await Response.Body.WriteAsync(salida.Cuerpo, ct);
        return new EmptyResult();
    }

    /// <summary>Un fallo de la puerta como problem+json, con <c>code</c> y <c>transient</c> como un rechazo.</summary>
    private static ContentResult Fallo(FalloDeLaPuerta fallo) => new()
    {
        StatusCode = fallo.Estado,
        ContentType = "application/problem+json",
        Content = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(fallo.Estado),
            status = fallo.Estado,
            detail = fallo.Mensaje,
            code = fallo.Codigo,
            transient = fallo.Transitorio,
        }, Web),
    };
}
