using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Exposes <c>GET /_health</c> as a JSON aggregate of every registered
/// <see cref="ISchemaHealthProbe"/>.
/// </summary>
/// <remarks>
/// Per ADR 0008 and the migration plan (04 §D.3), the endpoint is the
/// single operational surface used by load balancers, CI smoke tests,
/// and humans to confirm schema consistency. Status codes:
/// <list type="bullet">
///   <item>200 — every probe reports healthy.</item>
///   <item>503 — at least one probe reports unhealthy.</item>
/// </list>
/// </remarks>
[ApiController]
[Route("_health")]
public sealed class HealthController : ControllerBase
{
    private readonly IEnumerable<ISchemaHealthProbe> _probes;

    public HealthController(IEnumerable<ISchemaHealthProbe> probes) =>
        _probes = probes;

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var checks = new List<SchemaHealthResult>();
        foreach (var probe in _probes)
        {
            var result = await probe.CheckAsync(ct).ConfigureAwait(false);
            checks.Add(result);
        }

        var allHealthy = checks.Count == 0 || checks.All(r => r.IsHealthy);
        var statusCode = allHealthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var payload = new
        {
            status = allHealthy ? "healthy" : "unhealthy",
            checks = checks.Select(r => new
            {
                name = r.Name,
                healthy = r.IsHealthy,
                message = r.Message,
                // Cap-290 Batch A — opcional; null cuando el probe no
                // reporta metadata estructurada. Ops dashboards consumen.
                details = r.Details,
            }),
        };

        return StatusCode(statusCode, payload);
    }
}
