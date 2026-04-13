namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Configuración de evento de analytics. El renderer emite el evento
/// cuando el componente alcanza el estado relevante (view, click, etc.).
/// </summary>
public sealed record BehaviorTrackingModel(
    string? EventName,
    string? EventCategory,
    string? EventLabel);
