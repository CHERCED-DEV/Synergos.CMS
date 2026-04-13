namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Feature flag controlado desde CMS. FeatureKey debe coincidir exactamente
/// con la clave registrada en el sistema de flags del frontend.
/// </summary>
public sealed record BehaviorFeatureFlagModel(
    string? FeatureKey,
    bool    IsEnabled);
