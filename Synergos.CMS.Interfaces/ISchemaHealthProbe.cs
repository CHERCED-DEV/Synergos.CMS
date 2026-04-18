namespace Synergos.CMS.Interfaces;

/// <summary>
/// Health probe contract for schema consistency. Consumed by the
/// <c>/_health</c> endpoint registered in the Web layer.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory) and
/// ADR 0008 (uSync hybrid source-of-truth). Concrete probes live in
/// <c>Synergos.CMS.Application</c>, e.g. <c>SchemaVersionProbe</c> that
/// inspects uSync XMLs and <c>UsyncFolderProbe</c> that verifies folder
/// readability. Multiple probes can be registered; the health endpoint
/// aggregates them.
/// </remarks>
public interface ISchemaHealthProbe
{
    /// <summary>
    /// Runs the probe and returns a structured result. Implementations
    /// MUST NOT throw for routine "unhealthy" situations — they return
    /// <c>IsHealthy = false</c> with a <c>Message</c>. Exceptions are
    /// reserved for unexpected infrastructure failures.
    /// </summary>
    Task<SchemaHealthResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Outcome of a schema health probe.
/// </summary>
/// <param name="Name">Short stable identifier of the probe (e.g. <c>"schema_version_match"</c>).</param>
/// <param name="IsHealthy">Probe verdict.</param>
/// <param name="Message">Optional human-readable detail — required when <paramref name="IsHealthy"/> is <c>false</c>.</param>
public sealed record SchemaHealthResult(string Name, bool IsHealthy, string? Message = null);
