namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resolves whether a named feature gate is enabled for the current context.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory) and ADR 0011
/// (Feature flags via typed config). The default implementation
/// <c>AppsettingsFeatureGate</c> reads <c>FeatureFlagsSettings</c> bound
/// from <c>appsettings.*.json</c>. Alternative providers (SaaS feature
/// flag services, per-user targeting, gradual rollout) live behind a
/// future ADR and plug in as alternate implementations of this seam.
/// Callers should reference flag names from the typed constants class
/// (planned: <c>Synergos.CMS.Application.Dto.Constants.FeatureGateKeys</c>),
/// not from bare strings.
/// </remarks>
public interface IFeatureGate
{
    /// <summary>
    /// Returns <c>true</c> if the named gate is enabled. Unknown gates
    /// return <c>false</c> (fail-closed by default).
    /// </summary>
    bool IsEnabled(string gate);
}
