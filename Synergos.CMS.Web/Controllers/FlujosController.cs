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
///   <item>401 <c>puerta.sesion_requerida</c>, salvo en lo que el flujo abre a invitados (<c>Invitado</c>, NB-3: sólo
///   lecturas sin sujeto); eso pasa por su freno, y si se pide de más, 429 <c>puerta.demasiadas_peticiones</c>.</item>
///   <item>403 <c>puerta.personal_requerido</c>: una operación del personal (P8) pedida por un miembro sin ninguno de los
///   <c>Roles</c> del flujo.</item>
///   <item>403 <c>puerta.origen_no_permitido</c>, 415 <c>puerta.tipo_no_soportado</c> y 413
///   <c>puerta.cuerpo_demasiado_grande</c> (<see cref="FiltroMismoOrigenJson"/>).</item>
///   <item>400 <c>puerta.llave_requerida</c>, 400 <c>puerta.parametro_requerido</c> y 400
///   <c>puerta.parametro_invalido</c> (un parámetro de ruta que es «.» o «..»).</item>
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

    private readonly AvisoEnVivoDeLaPuerta? _enVivo;
    private readonly FrenoDeLaPuerta? _freno;

    /// <param name="sitio">El nombre del sitio de la petición, para el aviso. Nulo: el host.</param>
    /// <param name="enVivo">Los avisos en vivo que declaran los flujos (núcleo N3). Nulo: no se avisa.</param>
    /// <param name="freno">El freno de lo abierto a invitados (NB-3), un singleton. Nulo: no se abre nada a invitados.</param>
    public FlujosController(
        TablaDeLaPuerta tabla,
        IOptionsMonitor<PuertaSettings> ajustes,
        DestinosDeLaPuerta destinos,
        IMemberAccessGate miembro,
        ReenvioDeLaPuerta reenvio,
        SeccionesDelSitio? secciones = null,
        SitioDeLaPeticion? sitio = null,
        AvisoEnVivoDeLaPuerta? enVivo = null,
        FrenoDeLaPuerta? freno = null)
    {
        _tabla = tabla;
        _ajustes = ajustes;
        _destinos = destinos;
        _miembro = miembro;
        _reenvio = reenvio;
        _secciones = secciones;
        _sitio = () => sitio?.Resolver().SiteRoot?.Name;
        _enVivo = enVivo;
        _freno = freno;
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
        // Sin sesión sólo pasa lo que el flujo abre a invitados (NB-3), y sólo con freno: sin él, falla cerrada.
        Guid? miembro = _miembro.IsAuthenticated && _miembro.CurrentMemberKey is { } clave && clave != Guid.Empty ? clave : null;
        var abierta = _freno is not null && !op.Personal && ajustes.Invitado.Contains(op.Operacion, StringComparer.Ordinal);
        if (miembro is null && !abierta)
        {
            return Fallo(new FalloDeLaPuerta(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida",
                "Hace falta iniciar sesión."));
        }
        // Una operación del personal (P8): el rol del miembro, que es lo que reemplaza a la regla de dueño de la original.
        // Sin roles declarados no pasa nadie: el arranque ya no lo deja, y si llegara, falla cerrada. Uno en blanco no cuenta:
        // HasAnyRole con la lista vacía deja pasar a cualquier miembro.
        var roles = ajustes.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (op.Personal && (roles.Count == 0 || !_miembro.HasAnyRole(string.Join(',', roles))))
        {
            return Fallo(new FalloDeLaPuerta(StatusCodes.Status403Forbidden, "puerta.personal_requerido",
                "Esta operación es del personal del negocio."));
        }
        if (abierta)
        {
            var quien = miembro is { } m ? $"miembro:{m:N}" : $"ip:{HttpContext.Connection.RemoteIpAddress}";
            if (!_freno!.Permitir(op.Flujo, op.Operacion, quien, ajustes.FrenoPorMinuto ?? AccesoDeLaPuerta.FrenoPorDefecto, out var espera))
            {
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Fallo(new FalloDeLaPuerta(StatusCodes.Status429TooManyRequests, "puerta.demasiadas_peticiones",
                    "Demasiadas peticiones seguidas: vuelve a intentarlo en un momento.", Transitorio: true));
            }
        }
        if (FiltroMismoOrigenJson.Origen(Request) is { } ajeno) return Fallo(ajeno);

        byte[]? cuerpo = null;
        if (op.ConCuerpo)
        {
            if (FiltroMismoOrigenJson.Tipo(Request) is { } tipo) return Fallo(tipo);
            (cuerpo, var grande) = await FiltroMismoOrigenJson.LeerCuerpoAsync(Request, ct);
            if (grande is not null) return Fallo(grande);
        }

        // El sujeto es el miembro, o —si el flujo lo declara— el seudónimo del correo de la sesión (NE-4). Sólo lo
        // necesita la operación que lo pone o que reemite la llave con él; una lectura abierta a invitados, no (NB-3).
        string sujeto = string.Empty;
        if (op.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto) || op.LargoDeLaLlave is not null)
        {
            var porCorreo = SujetoDeLaPuerta.Normalizar(ajustes.SujetoDe) == SujetoDeLaPuerta.Correo;
            if (miembro is not { } dueno || (porCorreo && _miembro.CurrentMemberEmail is not { Length: > 0 }))
            {
                return Fallo(new FalloDeLaPuerta(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida",
                    porCorreo ? "Hace falta una sesión con correo." : "Hace falta iniciar sesión."));
            }
            sujeto = porCorreo
                ? LoQuePoneLaPuerta.SujetoPorCorreo(ajustes.SujetoKind ?? string.Empty, _miembro.CurrentMemberEmail!)
                : LoQuePoneLaPuerta.Sujeto(ajustes.SujetoKind ?? string.Empty, dueno);
        }
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
            if (string.IsNullOrEmpty(valor))
            {
                if (p.Requerido || p.EnLaRuta)
                {
                    return Fallo(new FalloDeLaPuerta(StatusCodes.Status400BadRequest, "puerta.parametro_requerido",
                        $"Hace falta «{p.Nombre}» en la consulta."));
                }
                continue;
            }

            // Un valor que va EN LA RUTA no puede ser un segmento de punto: escaparlo no lo cambia, y al
            // resolverlo contra la dirección del orquestador movería la petición a otra ruta que la tabla
            // no marcó. La consulta ya llega decodificada, así que «%2E%2E» también cae acá.
            if (p.EnLaRuta && ReenvioDeLaPuerta.EsSegmentoDePunto(valor))
            {
                return Fallo(new FalloDeLaPuerta(StatusCodes.Status400BadRequest, "puerta.parametro_invalido",
                    $"«{p.Nombre}» no es un valor válido."));
            }
            parametros[p.Nombre] = valor;
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
                LoQuePoneLaPuerta.Enlace(origen, RutaDelAviso(ajustes.Aviso, parametros), parametros),
                _sitio() ?? Request.Host.Host);
        }

        var salida = await _reenvio.EnviarAsync(op, parametros, cuerpo, cabeceras, ct);
        if (salida.Fallo is not null) return Fallo(salida.Fallo);

        Response.StatusCode = salida.Estado;
        if (salida.TipoDeContenido is not null) Response.ContentType = salida.TipoDeContenido;
        await Response.Body.WriteAsync(salida.Cuerpo, ct);

        // Lo que el flujo declara avisar en vivo, sólo si salió bien y DESPUÉS de contestar (NE-6). No lanza.
        if (_enVivo is not null && salida.Estado is >= 200 and < 300)
        {
            await _enVivo.PublicarAsync(op.Flujo, ajustes, op.Operacion, parametros, salida.Cuerpo, ct);
        }
        return new EmptyResult();
    }

    /// <summary>
    /// La ruta del enlace del aviso (D-006-AVISO): la del SITIO de la petición si el flujo la declara por sitio y ésta es
    /// una ruta del sitio que se puede armar con los parámetros de la operación; si no, la del flujo. Nunca un enlace a
    /// una página que el sitio no tiene.
    /// </summary>
    private string? RutaDelAviso(AvisoDelFlujoSettings? aviso, IReadOnlyDictionary<string, string> parametros)
    {
        if (aviso is { Seccion: { } seccion, Campo: { } campo }
            && _secciones?.Actual(seccion) is { } negocio
            && LoQuePoneLaPuerta.Propiedad(negocio.GetType(), campo)?.GetValue(negocio) is string { Length: > 1 } delSitio
            && delSitio[0] == '/' && !delSitio.StartsWith("//", StringComparison.Ordinal)
            && LoQuePoneLaPuerta.Enlace(string.Empty, delSitio, parametros) is not null)
        {
            return delSitio;
        }
        return aviso?.Ruta;
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
