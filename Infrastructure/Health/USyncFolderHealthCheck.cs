using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Synergos.CMS.Infrastructure.Health;

/// <summary>
/// Verifies the <c>uSync/v9/</c> folder is present in the content root — the
/// export target used for uSync backup. Absence is not fatal (uSync can be
/// disabled) but it is worth surfacing for ops.
///
/// Tagged <c>ready</c> so it surfaces on <c>/readyz</c> only. Returns
/// <c>Degraded</c>, never <c>Unhealthy</c>, because the site still renders
/// without uSync configured.
/// </summary>
public sealed class USyncFolderHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _env;

    public USyncFolderHealthCheck(IWebHostEnvironment env) => _env = env;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_env.ContentRootPath, "uSync", "v9");
        if (Directory.Exists(path))
            return Task.FromResult(HealthCheckResult.Healthy($"uSync folder present at {path}."));

        return Task.FromResult(HealthCheckResult.Degraded(
            $"uSync folder not found at {path}. Export has not been run or the path was removed."));
    }
}
