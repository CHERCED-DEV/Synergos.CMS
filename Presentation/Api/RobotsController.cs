using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;

namespace Synergos.CMS.Presentation.Api;

/// <summary>
/// Emits <c>/robots.txt</c> from configuration. Three modes:
///
/// <list type="bullet">
/// <item><b>Production</b> — allow all, publish sitemap URL.</item>
/// <item><b>Non-production</b> — disallow everything. Prevents accidental
///       indexing of staging/dev/preview environments.</item>
/// <item><b>Override</b> — editor-supplied verbatim body from
///       <c>Synergos:Runtime.RobotsTxtOverride</c>.</item>
/// </list>
///
/// Cached 30 minutes.
/// </summary>
[ApiController]
public sealed class RobotsController : ControllerBase
{
    private readonly IOptions<RuntimeSettings> _runtime;
    private readonly IWebHostEnvironment       _env;

    public RobotsController(IOptions<RuntimeSettings> runtime, IWebHostEnvironment env)
    {
        _runtime = runtime;
        _env     = env;
    }

    [HttpGet("/robots.txt")]
    [OutputCache(PolicyName = "SiteSettings")]
    [Produces("text/plain")]
    public IActionResult Index()
    {
        var rt = _runtime.Value;

        // Explicit override wins everything else.
        if (!string.IsNullOrWhiteSpace(rt.RobotsTxtOverride))
            return Content(rt.RobotsTxtOverride, "text/plain");

        if (!_env.IsProduction())
        {
            return Content(
                $"""
                # Synergos CMS — non-production environment ({_env.EnvironmentName})
                # All user agents are blocked to prevent indexing of staging content.
                User-agent: *
                Disallow: /
                """,
                "text/plain");
        }

        var host = Request.Host.HasValue ? $"{Request.Scheme}://{Request.Host}" : rt.WebHost;
        return Content(
            $"""
            # Synergos CMS — production
            User-agent: *
            Allow: /
            Disallow: /umbraco/
            Disallow: /App_Plugins/
            Disallow: /api/

            Sitemap: {host.TrimEnd('/')}/sitemap.xml
            """,
            "text/plain");
    }
}
