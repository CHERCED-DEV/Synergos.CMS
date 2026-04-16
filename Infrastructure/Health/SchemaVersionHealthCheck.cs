using Microsoft.Extensions.Diagnostics.HealthChecks;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Health;

/// <summary>
/// Verifies the schema version stored in Umbraco's key/value store matches
/// <see cref="SchemaVersion.Value"/>. Returns <c>Degraded</c> when they differ
/// — either the pipeline has not run yet (DB at old version) or a bump was
/// made but never applied (code ahead of DB).
///
/// Returns <c>Healthy</c> when both sides match. Tagged <c>ready</c> so it
/// reports only on <c>/readyz</c>; startup might briefly be in a mismatch
/// state and we do not want that to evict the instance from the load balancer.
/// </summary>
public sealed class SchemaVersionHealthCheck : IHealthCheck
{
    private readonly IKeyValueService _keyValue;

    public SchemaVersionHealthCheck(IKeyValueService keyValue) => _keyValue = keyValue;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var stored = _keyValue.GetValue(SchemaVersion.Key) ?? "none";
            if (string.Equals(stored, SchemaVersion.Value, StringComparison.Ordinal))
            {
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"Schema version {SchemaVersion.Value} applied."));
            }

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Schema mismatch: DB={stored}, expected={SchemaVersion.Value}. Pipeline will run on next startup."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Could not read schema version.", ex));
        }
    }
}
