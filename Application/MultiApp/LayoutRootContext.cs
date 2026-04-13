using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.MultiApp;

public sealed record LayoutRootContext(
    IPublishedContent? CurrentContent,
    IPublishedContent? ApplicationRoot,
    IPublishedContent? NavigationRoot,
    IPublishedContent? FooterRoot);
