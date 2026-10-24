using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Academy;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// El registro de las matrículas hechas por la puerta (010-educacion-matricula): lo que el CMS agrega a una matrícula
/// que abre, cobra y cierra el orquestador de Educación, para que el aula y «mi aprendizaje» la vean.
/// </summary>
/// <remarks>
/// <code>
/// POST /api/academy/puerta/{id}/anotar   → { enrollmentId, courseId, yaEstaba }   🔒 sesión · mismo origen
/// </code>
/// La matrícula la abre y la cierra la PUERTA (<c>/api/flujos/educacion.matricula/…</c>); esta ruta no la toca: la lee
/// al orquestador con el sujeto del miembro de la sesión, así que una ajena es un 404. La ruta directa de la academia
/// (<c>/api/academy/enroll|confirm</c>) sigue intacta en su controlador. Nadie guarda la respuesta.
/// </remarks>
[ApiController]
[Route("api/academy/puerta")]
public sealed class MatriculasDeLaPuertaController : ControllerBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly RegistroDeMatriculasDeLaPuerta _registro;
    private readonly IMemberAccessGate _gate;

    public MatriculasDeLaPuertaController(RegistroDeMatriculasDeLaPuerta registro, IMemberAccessGate gate)
    {
        _registro = registro;
        _gate = gate;
    }

    [HttpPost("{id}/anotar")]
    public async Task<IActionResult> Anotar(string id, CancellationToken cancellationToken)
    {
        SinCache();
        if (Miembro() is { } sinSesion) return sinSesion;
        if (FiltroMismoOrigenJson.Origen(Request) is { } ajeno) return Problema(ajeno.Estado, ajeno.Codigo, ajeno.Mensaje, ajeno.Transitorio);

        var r = await _registro.AnotarAsync(id, _gate.CurrentMemberKey!.Value, _gate.CurrentMemberDisplayName, _gate.CurrentMemberEmail, cancellationToken);
        return r.Bien
            ? Ok(new { enrollmentId = r.Anotacion!.EnrollmentId, courseId = r.Anotacion.CourseId, yaEstaba = r.Anotacion.YaEstaba })
            : Problema(r.Estado, r.Codigo ?? "academy.registro", r.Mensaje ?? string.Empty, r.Transitorio);
    }

    /// <summary>Que nadie guarde la respuesta: es la matrícula de una persona. También el rechazo.</summary>
    private void SinCache()
    {
        if (HttpContext is { } http) http.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>El miembro de la sesión con su <c>MemberKey</c>, que es el sujeto de sus matrículas; si no, 401.</summary>
    private ContentResult? Miembro()
        => _gate.IsAuthenticated && _gate.CurrentMemberKey is { } k && k != Guid.Empty
            ? null
            : Problema(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida", "Hace falta iniciar sesión.", false);

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
}
