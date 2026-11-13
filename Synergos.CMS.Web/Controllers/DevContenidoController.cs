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

    /// <summary>
    /// Importa fotos reales ya en disco según los manifiestos de la carpeta (Tienda, Booking, fichas de inmueble y medios
    /// por nombre). <c>POST /dev/fotos?carpeta=C:\...</c>. No descarga nada.
    /// </summary>
    /// <remarks>Idempotente: un medio que ya tiene esa licencia y ese crédito no se toca. Se exporta con uSync.</remarks>
    [HttpPost("fotos")]
    public IActionResult Fotos([FromQuery] string carpeta)
    {
        _logger.LogInformation("Dev endpoint fotos invocado.");
        if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta))
        {
            return BadRequest(new { error = "La carpeta no existe." });
        }
        var resultado = _contenido.ImportarFotos(carpeta);
        return resultado.Ok ? Ok(resultado) : Conflict(resultado);
    }

    /// <summary>
    /// Pone las fotos reales de una carpeta local (con su <c>manifiesto.json</c>) en los productos de la Tienda.
    /// <c>POST /dev/fotos-de-la-tienda?carpeta=C:\...</c>
    /// </summary>
    /// <remarks>Idempotente: un medio que ya tiene esa licencia y ese crédito no se toca. Se exporta con uSync.</remarks>
    [HttpPost("fotos-de-la-tienda")]
    public IActionResult FotosDeLaTienda([FromQuery] string carpeta)
    {
        _logger.LogInformation("Dev endpoint fotos-de-la-tienda invocado.");
        if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta))
        {
            return BadRequest(new { error = "La carpeta no existe." });
        }
        var resultado = _contenido.FotosDeLaTienda(carpeta);
        return resultado.Ok ? Ok(resultado) : Conflict(resultado);
    }
}
