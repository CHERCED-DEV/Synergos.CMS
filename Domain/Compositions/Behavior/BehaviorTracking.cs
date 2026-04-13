namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Analytics tracking composition — mirrors UI BehaviorTracking contract.
/// </summary>
public sealed record BehaviorTracking(
    string? EventName = null,
    string? EventCategory = null,
    string? EventLabel = null);
