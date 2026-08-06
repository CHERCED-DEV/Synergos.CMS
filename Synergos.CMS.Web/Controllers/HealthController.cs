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
///
/// <para><b>Y dice qué versión está contestando</b> (HU #19). El humo de un despliegue tiene que
/// poder distinguir «el sitio responde» de «el sitio responde <i>con lo que acabo de subir</i>»:
/// sin eso, un reinicio que falla en silencio deja viva la versión anterior y el despliegue se da
/// por bueno. El valor lo inyecta la imagen (<c>SYNERGOS_BUILD_SHA</c>, puesto por el
/// <c>Dockerfile</c> desde el SHA del commit); fuera de un contenedor no hay ninguno y se dice
/// así, en vez de inventar uno.</para>
/// </remarks>
[ApiController]
[Route("_health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>Lo que responde <c>version</c> cuando nadie puso el SHA — o sea, fuera de la imagen.</summary>
    public const string VersionDesconocida = "desconocida";

    private readonly IEnumerable<ISchemaHealthProbe> _probes;
    private readonly IConfiguration _config;

    public HealthController(IEnumerable<ISchemaHealthProbe> probes, IConfiguration config)
    {
        _probes = probes;
        _config = config;
    }

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

        var sha = _config["SYNERGOS_BUILD_SHA"];

        var payload = new
        {
            status = allHealthy ? "healthy" : "unhealthy",
            // Va FUERA de `checks` a propósito: no es una comprobación que pueda estar roja, es
            // la identidad de lo que está contestando. Meterla entre las probes la haría capaz
            // de poner el endpoint en 503, y una versión no es una condición de salud.
            version = string.IsNullOrWhiteSpace(sha) ? VersionDesconocida : sha,
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
