namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Interaction behavior composition — mirrors UI BehaviorInteraction contract.
/// </summary>
public sealed record BehaviorInteraction(
    string? InteractionType = null,
    string? InteractionAction = null);
