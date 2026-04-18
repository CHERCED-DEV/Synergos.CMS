using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IFeatureGate"/> backed by typed
/// <see cref="FeatureFlagsSettings"/> sourced from <c>appsettings</c>.
/// </summary>
/// <remarks>
/// Per ADR 0011, no external feature-flag service is used in this
/// phase. Unknown gate names resolve to <c>false</c>.
/// </remarks>
public sealed class AppsettingsFeatureGate(FeatureFlagsSettings settings) : IFeatureGate
{
    public bool IsEnabled(string gate) =>
        !string.IsNullOrEmpty(gate)
        && settings.Gates.TryGetValue(gate, out var enabled)
        && enabled;
}
