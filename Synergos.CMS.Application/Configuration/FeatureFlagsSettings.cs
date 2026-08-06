namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:FeatureFlags</c>. Feeds <c>AppsettingsFeatureGate</c>.
/// </summary>
/// <remarks>
/// Per ADR 0011 the source of feature flags is configuration — no
/// external SaaS. Each flag is a named bool under <see cref="Gates"/>.
/// Consumers reference flag names from the typed constants class
/// (planned: <c>Dto.Constants.FeatureGateKeys</c>) — not bare strings.
/// Missing keys resolve to <c>false</c> (fail-closed).
/// </remarks>
public sealed class FeatureFlagsSettings
{
    /// <summary>
    /// Case-insensitive map of gate name → enabled. Defaults to empty.
    /// </summary>
    public IDictionary<string, bool> Gates { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
}
