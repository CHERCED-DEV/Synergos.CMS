using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Web.Filters;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La herramienta que compone la tienda de la demo por páginas (014-despiece-tienda, I3). Va aparte de
/// <see cref="DevController"/> para no sumarle otra dependencia; vive en la misma ruta y con el mismo candado.
/// </summary>
[ApiController]
[Route("dev")]
[AllowAnonymous]
[DevSeedOnly]
public sealed class DevTiendaController : ControllerBase
{
    private readonly DevTiendaPorPaginas _tienda;
    private readonly ILogger<DevTiendaController> _logger;

    public DevTiendaController(DevTiendaPorPaginas tienda, ILogger<DevTiendaController> logger)
    {
        _tienda = tienda;
        _logger = logger;
    }

    /// <summary>
    /// Una página por vista de la tienda y de la consola, cada una con su bloque y su <c>vista</c>.
    /// <c>POST /dev/componer-la-tienda</c>
    /// </summary>
    /// <remarks>
    /// Idempotente: una página que ya existe no se toca. Lo creado se exporta con uSync (ADR 0129); un fallo sale con
    /// 409 y lo hecho hasta ahí.
    /// </remarks>
    [HttpPost("componer-la-tienda")]
    public IActionResult ComponerLaTienda()
    {
        _logger.LogInformation("Dev endpoint componer-la-tienda invocado.");
        var resultado = _tienda.Componer();
        return resultado.Ok ? Ok(resultado) : Conflict(resultado);
    }

    /// <summary>Las ofertas de muestra de la copia piloto (014-despiece-tienda, I5): «Precio antes» en cuatro productos.</summary>
    [HttpPost("ofertas-de-muestra")]
    public IActionResult OfertasDeMuestra()
    {
        _logger.LogInformation("Dev endpoint ofertas-de-muestra invocado.");
        var resultado = _tienda.MarcarOfertasDeMuestra();
        return resultado.Ok ? Ok(resultado) : Conflict(resultado);
    }
}
