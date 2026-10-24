using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Academy;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Republicar la oferta del catálogo entero de la academia en su orquestador (010-educacion-matricula), a mano.
/// </summary>
/// <remarks>
/// <code>
/// POST /api/academy/oferta/republicar → { total, publicados, fallidos, sinDestino }   🔒 admin
/// </code>
/// Recorre el catálogo activo y publica la oferta de cada curso: es la reparación de un publicar que no llegó y la
/// forma de poner a matricular la siembra de la demo, que ningún editor publica. NUNCA corre al arrancar (ADR 0013): lo
/// invoca una persona. Sólo <c>admin</c> —un instructor no—, porque escribe la oferta de TODOS los cursos. El molde de
/// <c>POST /api/shop/oferta/republicar</c>.
/// </remarks>
[ApiController]
[Route("api/academy/oferta")]
public sealed class OfertaDeCursosController : ControllerBase
{
    private readonly ICourseCatalogProvider _catalogo;
    private readonly IPublicadorDeOfertasDeCursos _ofertas;
    private readonly IMemberAccessGate _gate;
    private readonly ILogger<OfertaDeCursosController> _log;

    public OfertaDeCursosController(
        ICourseCatalogProvider catalogo, IPublicadorDeOfertasDeCursos ofertas, IMemberAccessGate gate, ILogger<OfertaDeCursosController> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _gate = gate;
        _log = log;
    }

    [HttpPost("republicar")]
    public async Task<IActionResult> Republicar(CancellationToken cancellationToken)
    {
        if (!_gate.IsAuthenticated)
        {
            return Unauthorized(new { error = "Inicie sesión como administrador de la academia." });
        }
        if (!_gate.HasAnyRole("admin"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Republicar la oferta es del administrador de la academia." });
        }

        // El catálogo de cursos no pagina: la búsqueda sin filtros es el catálogo entero.
        var resultado = await _catalogo.SearchAsync(new CourseQuery(), cancellationToken);
        var ids = resultado.Courses.Select(c => c.Id).Distinct(StringComparer.Ordinal).ToList();

        int publicados = 0, fallidos = 0, sinDestino = 0;
        foreach (var id in ids)
        {
            var curso = await _catalogo.GetCourseAsync(id, cancellationToken);
            if (curso is null)
            {
                fallidos++;
                _log.LogWarning("Republicar: el catálogo lista {Curso} y no sirve su ficha.", id);
                continue;
            }

            switch (await _ofertas.PublicarAsync(curso, cancellationToken))
            {
                case EstadoDeLaOfertaDeCurso.Hecha: publicados++; break;
                case EstadoDeLaOfertaDeCurso.SinDestino: sinDestino++; break;
                default: fallidos++; break;
            }
        }

        return Ok(new RepublicarOfertaResponse(ids.Count, publicados, fallidos, sinDestino));
    }

    /// <summary>Cómo terminó republicar: cuántos cursos se recorrieron y qué pasó con cada uno.</summary>
    public sealed record RepublicarOfertaResponse(int Total, int Publicados, int Fallidos, int SinDestino);
}
