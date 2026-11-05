using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Web.Filters;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La herramienta que compone la red social de la demo por páginas (012-blogs-social, I3). Va aparte de
/// <see cref="DevController"/>, como la de la tienda (<see cref="DevTiendaController"/>): vive en la misma ruta y con el
/// mismo candado.
/// </summary>
[ApiController]
[Route("dev")]
[AllowAnonymous]
[DevSeedOnly]
public sealed class DevBlogsController : ControllerBase
{
    private readonly DevBlogsPorPaginas _blogs;
    private readonly ILogger<DevBlogsController> _logger;

    public DevBlogsController(DevBlogsPorPaginas blogs, ILogger<DevBlogsController> logger)
    {
        _blogs = blogs;
        _logger = logger;
    }

    /// <summary>
    /// Una página por vista de la red bajo Blogs, cada una con su bloque y su <c>vista</c>.
    /// <c>POST /dev/componer-blogs</c>
    /// </summary>
    /// <remarks>
    /// Idempotente: una página que ya existe no se toca. Lo creado se exporta con uSync (ADR 0129); un fallo sale con
    /// 409 y lo hecho hasta ahí.
    /// </remarks>
    [HttpPost("componer-blogs")]
    public IActionResult ComponerBlogs()
    {
        _logger.LogInformation("Dev endpoint componer-blogs invocado.");
        var resultado = _blogs.Componer();
        return resultado.Ok ? Ok(resultado) : Conflict(resultado);
    }
}
