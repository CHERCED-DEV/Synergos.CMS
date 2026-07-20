using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Dev-only endpoints para content seeding y smoke-test. Todos gated
/// por <c>Synergos:DevSeed:Enabled=true</c>. Retornan 404 cuando el
/// flag está off (no-op en prod).
/// </summary>
[ApiController]
[Route("dev")]
[AllowAnonymous]
public sealed class DevController : ControllerBase
{
    private readonly DevSeedSettings _settings;
    private readonly DevTestContentSeeder _seeder;
    private readonly SynergosIdentitySeeder _identitySeeder;
    private readonly DevContentFiller _filler;
    private readonly DevMemberRoleSeeder _roleSeeder;
    private readonly ILogger<DevController> _logger;

    public DevController(
        IOptions<DevSeedSettings> settings,
        DevTestContentSeeder seeder,
        SynergosIdentitySeeder identitySeeder,
        DevContentFiller filler,
        DevMemberRoleSeeder roleSeeder,
        ILogger<DevController> logger)
    {
        _settings = settings.Value;
        _seeder = seeder;
        _identitySeeder = identitySeeder;
        _filler = filler;
        _roleSeeder = roleSeeder;
        _logger = logger;
    }

    [HttpPost("seed-test-site")]
    public IActionResult SeedTestSite()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("DevSeed endpoint invocado.");
        var result = _seeder.Seed();
        return Ok(result);
    }

    [HttpDelete("clear-test-site")]
    public IActionResult ClearTestSite()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("DevClear endpoint invocado.");
        var result = _seeder.Clear();
        return Ok(result);
    }

    [HttpPost("clear-all-content")]
    public IActionResult ClearAllContent()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogWarning("DevClearAll endpoint invocado — borra TODO el content tree.");
        var result = _identitySeeder.ClearAll();
        return Ok(result);
    }

    [HttpPost("seed-synergos-identity")]
    public IActionResult SeedSynergosIdentity()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("SynergosSeed endpoint invocado.");
        var result = _identitySeeder.Seed();
        return Ok(result);
    }

    [HttpPost("fill-synergos-pages")]
    public IActionResult FillSynergosPages()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("DevContentFiller endpoint invocado (fill-synergos-pages).");
        var result = _filler.FillSynergosPages();
        return Ok(result);
    }

    /// <summary>
    /// Crea los member groups de dominio y (opcional) se los asigna a un member.
    /// <c>POST /dev/seed-member-roles?email=&amp;roles=funcionario,organizador</c>
    /// </summary>
    /// <remarks>
    /// Existe porque cuatro olas de seguridad dejaron consolas cerradas por rol
    /// (<c>funcionario</c>, <c>organizador</c>, <c>doctor</c>) que <b>no se podían
    /// demostrar</b> sin crear los grupos a mano en el backoffice. NO crea members ni
    /// toca contraseñas: solo reparte permisos de demo sobre identidades que ya existen.
    /// </remarks>
    [HttpPost("seed-member-roles")]
    public IActionResult SeedMemberRoles([FromQuery] string? email, [FromQuery] string? roles)
    {
        if (!_settings.Enabled) return NotFound();

        var requested = (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("DevSeed endpoint invocado (seed-member-roles).");
        var result = _roleSeeder.Seed(email, requested);
        return Ok(result);
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { ok = true, devSeedEnabled = _settings.Enabled, timestamp = DateTime.UtcNow });
    }
}
