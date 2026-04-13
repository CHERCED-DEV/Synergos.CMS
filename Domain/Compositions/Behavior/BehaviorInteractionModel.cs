namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Interacción declarativa: qué evento del usuario dispara qué acción.
/// El CMS declara el contrato — el frontend implementa el handler.
/// </summary>
public sealed record BehaviorInteractionModel(
    string? InteractionType,
    string? InteractionAction);
