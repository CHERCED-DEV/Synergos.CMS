using Synergos.CMS.Application;
using Umbraco.Extensions;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Builds navigation trees from Umbraco content.
/// Delegates to <see cref="LayoutContentResolver"/> for tree traversal,
/// then maps to clean <see cref="NavigationItem"/> domain models.
/// </summary>
public sealed class UmbracoNavigationService : INavigationService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoNavigationService(IContentContextAccessor accessor) => _accessor = accessor;

    public IReadOnlyList<NavigationItem> GetMainNavigation(int rootNodeId)
    {
        var context = LayoutContentResolver.Resolve(_accessor);

        // Use the explicit rootNodeId when provided — this is the NavigationGroup node
        // picked in SiteSettings.headerNavigation (or a layout-profile override).
        // Fall back to the context-resolved nav root only when rootNodeId is unresolvable.
        var navRoot = _accessor.GetById(rootNodeId)
                  ?? context.NavigationRoot
                  ?? context.ApplicationRoot;

        return MapNodes(LayoutContentResolver.BuildNavigationTree(navRoot, context.CurrentContent));
    }

    public IReadOnlyList<NavigationItem> GetFooterNavigation(int rootNodeId)
    {
        var context = LayoutContentResolver.Resolve(_accessor);

        var footerRoot = _accessor.GetById(rootNodeId)
                      ?? context.FooterRoot
                      ?? context.ApplicationRoot;

        return MapNodes(LayoutContentResolver.BuildNavigationTree(footerRoot, context.CurrentContent));
    }

    public IReadOnlyList<NavigationItem> GetBreadcrumbs(int currentNodeId)
    {
        var context = LayoutContentResolver.Resolve(_accessor);
        if (context.CurrentContent is null || context.ApplicationRoot is null)
            return [];

        return context.CurrentContent
            .AncestorsOrSelf()
            .Where(n => n.Level >= context.ApplicationRoot.Level)
            .OrderBy(n => n.Level)
            .Select(n => new NavigationItem(
                Title: LayoutContentResolver.ReadText(n, "navigationTitle", "pageTitle") ?? n.Name,
                Url: n.Url() ?? "#",
                IsActive: n.Id == context.CurrentContent.Id,
                IsVisible: true,
                Children: []))
            .ToList();
    }

    private static IReadOnlyList<NavigationItem> MapNodes(IReadOnlyList<LayoutNavigationNode> nodes)
        => nodes
            .Select(n => new NavigationItem(
                Title: n.Title,
                Url: n.Url,
                IsActive: n.IsActive,
                IsVisible: true,
                Children: MapNodes(n.Children)))
            .ToList();
}
