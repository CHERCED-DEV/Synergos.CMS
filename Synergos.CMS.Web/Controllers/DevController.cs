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
    private readonly ILogger<DevController> _logger;

    public DevController(
        IOptions<DevSeedSettings> settings,
        DevTestContentSeeder seeder,
        SynergosIdentitySeeder identitySeeder,
        DevContentFiller filler,
        ILogger<DevController> logger)
    {
        _settings = settings.Value;
        _seeder = seeder;
        _identitySeeder = identitySeeder;
        _filler = filler;
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

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { ok = true, devSeedEnabled = _settings.Enabled, timestamp = DateTime.UtcNow });
    }
}
