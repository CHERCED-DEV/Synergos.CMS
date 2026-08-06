using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Health probe that reports the declared schema version so operators
/// can confirm that code and deployed schema are aligned.
/// </summary>
/// <remarks>
/// Per ADR 0008, uSync XMLs are the source of truth for schema. This
/// probe returns the version string the code was built against. Future
/// iterations can compare against a value read from uSync metadata and
/// fail when they diverge; for now the probe only asserts a
/// non-empty version is declared.
/// </remarks>
public sealed class SchemaVersionProbe : ISchemaHealthProbe
{
    /// <summary>Sentinel used when the code carries no materialised schema yet.</summary>
    public const string InitialVersion = "0.0.0";

    public const string ProbeName = "schema_version";

    private readonly string _version;

    public SchemaVersionProbe() : this(InitialVersion) { }

    public SchemaVersionProbe(string version) =>
        _version = string.IsNullOrWhiteSpace(version) ? InitialVersion : version;

    public Task<SchemaHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var result = new SchemaHealthResult(
            Name: ProbeName,
            IsHealthy: true,
            Message: _version);
        return Task.FromResult(result);
    }
}
