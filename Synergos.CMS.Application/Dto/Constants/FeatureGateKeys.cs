namespace Synergos.CMS.Application.Dto.Constants;

/// <summary>
/// Typed constants for feature-gate names consumed by
/// <c>Synergos.CMS.Interfaces.IFeatureGate</c>. Each gate name appears
/// here exactly once; callers reference the constant, never a bare
/// string literal.
/// </summary>
/// <remarks>
/// Per ADR 0011, the source of values is <c>appsettings.*.json</c>
/// under <c>Synergos:FeatureFlags:Gates</c>, bound to
/// <c>FeatureFlagsSettings</c>.
///
/// Entries are added only when a concrete consumer introduces the gate.
/// No speculative flags.
/// </remarks>
public static class FeatureGateKeys
{
    // Flag constants are added as the product introduces gates.
    // Example (future): public const string AlphaUi = "alpha-ui";
}
