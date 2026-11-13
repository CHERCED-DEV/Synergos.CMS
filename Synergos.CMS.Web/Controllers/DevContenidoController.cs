using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Web.Filters;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La herramienta que le pone contenido real a las portadas de las verticales (016-contenido-piezas). Va aparte de
/// <see cref="DevController"/> para no sumarle otra dependencia; vive en la misma ruta y con el mismo candado.
/// </summary>
[ApiController]
[Route("dev")]
[AllowAnonymous]
[DevSeedOnly]
public sealed class DevContenidoController : ControllerBase
{
    private readonly DevContenidoDeVerticales _contenido;
    private readonly ILogger<DevContenidoController> _logger;

    public DevContenidoController(DevContenidoDeVerticales contenido, ILogger<DevContenidoController> logger)
    {
        _contenido = contenido;
        _logger = logger;
    }

    /// <summary>
    /// Suma el contenido de una vertical al final de su portada. <c>POST /dev/contenido/{vertical}</c>
    /// </summary>
    /// <remarks>Idempotente: si la portada ya lo tiene, no se toca. Lo creado se exporta con uSync (ADR 0129).</remarks>
    [HttpPost("contenido/{vertical}")]
    public IActionResult Componer(string vertical)
    {
        _logger.LogInformation("Dev endpoint contenido/{Vertical} invocado.", vertical);
        var resultado = _contenido.Componer(vertical);
        return resultado.Ok ? Ok(resultado) : Conflict(resultado);
    }
}
