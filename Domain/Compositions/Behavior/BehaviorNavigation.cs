namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Navigation behavior composition — mirrors UI BehaviorNavigation contract.
/// </summary>
public sealed record BehaviorNavigation(
    string? NavigateTo = null,
    string? NavigationType = null);
