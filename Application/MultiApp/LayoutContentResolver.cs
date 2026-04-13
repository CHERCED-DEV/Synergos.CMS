using System.Collections;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.MultiApp;

public static class LayoutContentResolver
{
    public static LayoutRootContext Resolve(IContentContextAccessor accessor, IPublishedContent? currentContent = null)
    {
        var requestContent = currentContent ?? accessor.GetCurrentPage();
        var rootFallback   = accessor.GetAtRoot().FirstOrDefault();

        requestContent ??= rootFallback;

        var applicationRoot = ResolveApplicationRoot(requestContent) ?? rootFallback;

        var navigationRoot = applicationRoot is null
            ? null
            : ReadPickerContent(applicationRoot, "headerNavigation") ?? applicationRoot;

        var footerRoot = applicationRoot is null
            ? null
            : ReadPickerContent(applicationRoot, "footerNavigation") ?? applicationRoot;

        return new LayoutRootContext(requestContent, applicationRoot, navigationRoot, footerRoot);
    }

    public static IReadOnlyList<IPublishedContent> BuildVisibleChildren(IPublishedContent? root)
    {
        if (root is null)
        {
            return [];
        }

        return root.Children
            .Where(ShouldDisplayInNavigation)
            .OrderBy(child => child.SortOrder)
            .ThenBy(child => child.Name)
            .ToList();
    }

    public static IReadOnlyList<LayoutNavigationNode> BuildNavigationTree(
        IPublishedContent? root,
        IPublishedContent? currentContent)
    {
        if (root is null)
        {
            return [];
        }

        return BuildVisibleChildren(root)
            .Select(child => BuildNavigationItem(child, currentContent))
            .Where(item => item is not null)
            .Cast<LayoutNavigationNode>()
            .ToList();
    }

    public static string? ReadText(IPublishedContent content, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!content.HasProperty(alias))
            {
                continue;
            }

            var value = content.Value<string>(alias);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static string? ReadHtml(IPublishedContent content, params string[] aliases)
        => ReadText(content, aliases);

    public static bool ReadBool(IPublishedContent content, string alias, bool defaultValue = false)
        => content.HasProperty(alias)
            ? content.Value<bool>(alias)
            : defaultValue;

    public static string? ReadMediaUrl(IPublishedContent content, params string[] aliases)
    {
        var media = ReadPickerContent(content, aliases);
        return media?.Url();
    }

    public static IPublishedContent? ReadPickerContent(IPublishedContent content, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!content.HasProperty(alias))
            {
                continue;
            }

            var value = content.Value<object?>(alias);
            var publishedContent = ExtractPublishedContent(value);
            if (publishedContent is not null)
            {
                return publishedContent;
            }
        }

        return null;
    }

    private static LayoutNavigationNode? BuildNavigationItem(IPublishedContent content, IPublishedContent? currentContent)
    {
        if (!ShouldDisplayInNavigation(content))
        {
            return null;
        }

        var children = BuildVisibleChildren(content)
            .Select(child => BuildNavigationItem(child, currentContent))
            .Where(item => item is not null)
            .Cast<LayoutNavigationNode>()
            .ToList();

        var url = content.Url();
        var title = ReadText(content, "navigationTitle", "pageTitle", "title") ?? content.Name;
        var isActive = currentContent is not null && content.IsAncestorOrSelf(currentContent);

        return new LayoutNavigationNode(
            Title: title,
            Url: string.IsNullOrWhiteSpace(url) ? "#" : url,
            IsActive: isActive,
            Children: children);
    }

    private static IPublishedContent? ResolveApplicationRoot(IPublishedContent? currentContent)
    {
        if (currentContent is null)
            return null;

        // Primary: look for a node with the explicit layoutIsApplicationRoot flag.
        var appRoot = currentContent.AncestorsOrSelf()
            .OrderByDescending(node => node.Level)
            .FirstOrDefault(node => ReadBool(node, LayoutAliases.ApplicationRootFlag));

        if (appRoot is not null)
            return appRoot;

        // Fallback: detect SiteRoot by content type alias.
        // This covers the common case where the backoffice flag is not set.
        return currentContent.AncestorsOrSelf()
                   .FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot)
               ?? currentContent.Root();
    }

    // Infrastructure types that must never appear in the site navigation,
    // regardless of their umbracoNaviHide value.
    private static readonly HashSet<string> _navExcludedAliases =
    [
        ContentTypeKeys.Aliases.PlatformRoot,
        ContentTypeKeys.Aliases.GlobalSettings,
        ContentTypeKeys.Aliases.SharedContentFolder,
        ContentTypeKeys.Aliases.SiteSettingsAlias,
        ContentTypeKeys.Aliases.ThemeSettings,
        ContentTypeKeys.Aliases.PageTagsFolder,
        ContentTypeKeys.Aliases.NavigationGroup,
        ContentTypeKeys.Aliases.ReusableBlock,
        ContentTypeKeys.Aliases.FormDefinition,
    ];

    private static bool ShouldDisplayInNavigation(IPublishedContent content)
        => !ReadBool(content, "umbracoNaviHide")
           && content.IsVisible()
           && !_navExcludedAliases.Contains(content.ContentType.Alias);

    private static IPublishedContent? ExtractPublishedContent(object? value)
    {
        switch (value)
        {
            case IPublishedContent publishedContent:
                return publishedContent;
            case IEnumerable<IPublishedContent> publishedContents:
                return publishedContents.FirstOrDefault();
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is IPublishedContent publishedContent)
                {
                    return publishedContent;
                }
            }
        }

        return null;
    }
}
