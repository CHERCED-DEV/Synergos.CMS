namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Badge composition — mirrors UI ContentBadge contract.
/// </summary>
public sealed record ContentBadge(
    string? BadgeText = null,
    string? BadgeType = null);
