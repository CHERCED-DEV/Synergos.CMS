using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Emite <c>/robots.txt</c> dinámico con referencia al sitemap.xml
/// del host actual. En entornos no-Production el robots se emite con
/// <c>Disallow: /</c> para evitar indexación accidental de previews
/// y staging.
/// </summary>
[ApiController]
[Route("robots.txt")]
public sealed class RobotsController : ControllerBase
{
    private readonly IHostEnvironment _environment;

    public RobotsController(IHostEnvironment environment) =>
        _environment = environment;

    [HttpGet]
    [AllowAnonymous]
    public ContentResult GetRobots()
    {
        var hostBase = $"{Request.Scheme}://{Request.Host}";
        var isProduction = _environment.IsProduction();

        var body = isProduction
            ? $"""
              User-agent: *
              Allow: /
              Disallow: /umbraco/
              Disallow: /api/
              Disallow: /flow/

              Sitemap: {hostBase}/sitemap.xml
              """
            : $"""
              # Non-production environment ({_environment.EnvironmentName}) — discourage indexing.
              User-agent: *
              Disallow: /

              Sitemap: {hostBase}/sitemap.xml
              """;

        return Content(body, "text/plain; charset=utf-8");
    }
}
