using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Api;

/// <summary>
/// API JSON para consumo desde Angular / Web Components.
///
/// Expone el PageConfig de cualquier página publicada en Umbraco.
///
/// Endpoints:
///   GET /api/synergos/v1/page?path=/inicio
///   GET /api/synergos/v1/page?path=/servicios/consulting
///
/// El parámetro `path` es la URL relativa del nodo de contenido en Umbraco.
/// Devuelve 404 si no existe un nodo publicado en esa ruta.
///
/// El PageConfig resultante es el contrato Angular ↔ CMS:
///   { title, subtitle, seo, dom, sections: [{ type, blockClass, data }] }
/// </summary>
[ApiController]
[Route("api/synergos/v1")]
public sealed class SynergosPageApiController : UmbracoApiController
{
    private readonly IUmbracoContextAccessor _ctx;
    private readonly PageConfigAssembler     _assembler;

    public SynergosPageApiController(
        IUmbracoContextAccessor ctx,
        PageConfigAssembler     assembler)
    {
        _ctx       = ctx;
        _assembler = assembler;
    }

    /// <summary>
    /// Devuelve el PageConfig de la página en la ruta indicada.
    /// </summary>
    [HttpGet("page")]
    [Produces("application/json")]
    [OutputCache(Duration = 30, VaryByQueryKeys = ["path", "culture"])]
    public IActionResult GetPage([FromQuery] string path = "/", [FromQuery] string? culture = null)
    {
        var ctx  = _ctx.GetRequiredUmbracoContext();
        var page = ctx.Content?.GetByRoute(path, culture: culture);

        if (page is null)
            return NotFound(new { error = $"No published content found at path '{path}'." });

        return Ok(_assembler.Build(page));
    }
}
