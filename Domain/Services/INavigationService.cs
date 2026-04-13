namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Builds navigation trees for headers, footers, and breadcrumbs.
/// Independent of Umbraco — consumes and returns clean domain models.
/// </summary>
public interface INavigationService
{
    IReadOnlyList<NavigationItem> GetMainNavigation(int rootNodeId);
    IReadOnlyList<NavigationItem> GetFooterNavigation(int rootNodeId);
    IReadOnlyList<NavigationItem> GetBreadcrumbs(int currentNodeId);
}

/// <summary>A single navigation node with optional children.</summary>
public sealed record NavigationItem(
    string Title,
    string Url,
    bool IsActive,
    bool IsVisible,
    IReadOnlyList<NavigationItem> Children);
