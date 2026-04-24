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
    private readonly ILogger<DevController> _logger;

    public DevController(
        IOptions<DevSeedSettings> settings,
        DevTestContentSeeder seeder,
        ILogger<DevController> logger)
    {
        _settings = settings.Value;
        _seeder = seeder;
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

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { ok = true, devSeedEnabled = _settings.Enabled, timestamp = DateTime.UtcNow });
    }
}
