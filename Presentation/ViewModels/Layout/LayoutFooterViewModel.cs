using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Presentation.ViewModels.Layout;

public sealed record LayoutFooterViewModel(
    LayoutBrandViewModel Brand,
    LayoutThemeViewModel Theme,
    IReadOnlyList<LayoutNavigationItemViewModel> NavigationItems,
    string? FooterCopy,
    string? TrackingBodyScriptsHtml,
    string? ApplicationRootName,
    string? NavigationRootName,
    IReadOnlyList<SocialLinkViewModel> SocialLinks,
    string? NewsletterActionUrl,
    ContactInfoConfig? ContactInfo);

public sealed record SocialLinkViewModel(string Label, string Url, string Platform);
