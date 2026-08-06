using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Endpoints públicos de health probe para load balancers / orchestrators
/// (Kubernetes liveness/readiness probes, AWS ALB target group, GCP
/// instance group, etc). Diferenciado del <c>/admin/health</c>
/// member-gated que reporta diagnostics ricos — estos solo emiten
/// JSON minimal sin requerir auth.
/// </summary>
/// <remarks>
/// Convenciones:
/// - <c>GET /healthz</c> — <b>liveness</b>: HTTP 200 sí el proceso está
///   vivo. No toca Umbraco context ni DB. Usado por k8s
///   <c>livenessProbe</c> para detectar cuelgues fatales.
/// - <c>GET /healthz/ready</c> — <b>readiness</b>: HTTP 200 si Umbraco
///   ya cargó el published cache y puede atender requests; 503 si está
///   warming up. Usado por k8s <c>readinessProbe</c> y por load
///   balancers para sacar la instancia del pool durante deploy.
///
/// Respuesta JSON:
/// <code>
/// { "status": "ok|warming", "uptimeSeconds": 1234 }
/// </code>
///
/// Olas 142-143.
/// </remarks>
[ApiController]
[Route("healthz")]
public sealed class HealthzController : ControllerBase
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public HealthzController(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Liveness()
    {
        var uptimeSeconds = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
        return Ok(new { status = "ok", uptimeSeconds });
    }

    [HttpGet("ready")]
    [AllowAnonymous]
    public IActionResult Readiness()
    {
        var ready = _umbracoContextAccessor.TryGetUmbracoContext(out var ctx) && ctx.Content is not null;
        var uptimeSeconds = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
        if (!ready)
        {
            return StatusCode(503, new { status = "warming", uptimeSeconds });
        }
        return Ok(new { status = "ok", uptimeSeconds });
    }
}
