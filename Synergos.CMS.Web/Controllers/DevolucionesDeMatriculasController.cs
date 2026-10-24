using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Academy;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La devolución de una matrícula de la puerta por el personal (lote 8, 010-P2 B, T036), desde la consola del instructor.
/// </summary>
/// <remarks>
/// <code>
/// POST /api/academy/instructor/devolver { matricula }   → { matricula, devuelto, moneda }   🔒 admin · mismo origen
/// </code>
/// Abre el flujo posterior <c>educacion.devolucion</c> por el reenvío de la puerta (<see cref="DevolucionesDeMatriculas"/>)
/// y, completado, da de baja la matrícula en el libro. Sólo <c>admin</c>: un instructor ve a sus alumnos, no devuelve
/// plata. Un rechazo pasa con su código como problem+json. Ruta propia para no tocar el constructor de
/// <c>AcademyController</c>, como la oferta.
/// </remarks>
[ApiController]
[Route("api/academy/instructor")]
public sealed class DevolucionesDeMatriculasController : ControllerBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly DevolucionesDeMatriculas _devoluciones;
    private readonly IMemberAccessGate _gate;

    public DevolucionesDeMatriculasController(DevolucionesDeMatriculas devoluciones, IMemberAccessGate gate)
    {
        _devoluciones = devoluciones;
        _gate = gate;
    }

    [HttpPost("devolver")]
    public async Task<IActionResult> Devolver([FromBody] DevolverMatriculaRequest? pedido, CancellationToken cancellationToken)
    {
        if (HttpContext is { } http) http.Response.Headers.CacheControl = "no-store";
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not { } k || k == Guid.Empty)
        {
            return Problema(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida", "Hace falta iniciar sesión.", false);
        }
        if (!_gate.HasAnyRole("admin"))
        {
            return Problema(StatusCodes.Status403Forbidden, "puerta.personal_requerido", "Devolver una matrícula es del administrador de la academia.", false);
        }
        if (FiltroMismoOrigenJson.Origen(Request) is { } ajeno) return Problema(ajeno.Estado, ajeno.Codigo, ajeno.Mensaje, ajeno.Transitorio);
        if (string.IsNullOrWhiteSpace(pedido?.Matricula))
        {
            return Problema(StatusCodes.Status400BadRequest, "academy.devolucion_invalida", "Falta la matrícula que se devuelve.", false);
        }

        var r = await _devoluciones.DevolverAsync(pedido.Matricula, cancellationToken);
        return r.Bien
            ? Ok(new DevolverMatriculaResponse(pedido.Matricula.Trim(), r.Devuelto, r.Moneda))
            : Problema(EstadoDe(r), r.Codigo!, r.Mensaje ?? string.Empty, r.Transitorio);
    }

    /// <summary>El estado HTTP de un rechazo, por su clase: sesión, personal, no disponible, transitorio o conflicto.</summary>
    private static int EstadoDe(DevolucionDeLaMatricula r) => r.Codigo switch
    {
        "puerta.sesion_requerida" => StatusCodes.Status401Unauthorized,
        "puerta.personal_requerido" => StatusCodes.Status403Forbidden,
        "academy.devolucion_invalida" => StatusCodes.Status400BadRequest,
        "academy.devolucion_no_disponible" => StatusCodes.Status503ServiceUnavailable,
        _ when r.Transitorio => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };

    /// <summary>Un «no» como problem+json, con <c>code</c> y <c>transient</c> como la puerta.</summary>
    private static ContentResult Problema(int estado, string codigo, string mensaje, bool transitorio) => new()
    {
        StatusCode = estado,
        ContentType = "application/problem+json",
        Content = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(estado),
            status = estado,
            detail = mensaje,
            code = codigo,
            transient = transitorio,
        }, Web),
    };

    /// <summary>Qué matrícula se devuelve: el id de su saga, el que la consola recibe en la fila.</summary>
    public sealed record DevolverMatriculaRequest(string? Matricula);

    /// <summary>Lo devuelto: el total cobrado, en su moneda.</summary>
    public sealed record DevolverMatriculaResponse(string Matricula, decimal Devuelto, string Moneda);
}
