using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Presentation.ViewModels.Layout;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Presentation.ViewComponents;

public sealed class LayoutFooterViewComponent : ViewComponent
{
    private readonly ISiteResolver           _siteResolver;
    private readonly INavigationService      _navigationService;
    private readonly IThemeService           _themeService;
    private readonly IFooterService          _footerService;
    private readonly ITrackingService        _trackingService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public LayoutFooterViewComponent(
        ISiteResolver           siteResolver,
        INavigationService      navigationService,
        IThemeService           themeService,
        IFooterService          footerService,
        ITrackingService        trackingService,
        IUmbracoContextAccessor umbracoContextAccessor)
    {
        _siteResolver           = siteResolver;
        _navigationService      = navigationService;
        _themeService           = themeService;
        _footerService          = footerService;
        _trackingService        = trackingService;
        _umbracoContextAccessor = umbracoContextAccessor;
    }

    public Task<IViewComponentResult> InvokeAsync()
    {
        var site = _siteResolver.Resolve();

        // Per-page layout config (from CompDomLayoutProfile composition on the page)
        _umbracoContextAccessor.TryGetUmbracoContext(out var ctx);
        var currentPage = ctx?.PublishedRequest?.PublishedContent;
        var pageCfg     = _themeService.GetPageLayoutConfig(currentPage?.Id);

        if (!pageCfg.ShowFooter)
            return Task.FromResult<IViewComponentResult>(Content(string.Empty));

        var layout          = _themeService.GetLayoutConfig(site.RootNodeId);
        var footerCfg       = _footerService.GetSiteFooterConfig(site.RootNodeId);
        var contactInfo     = _footerService.GetContactInfo(site.RootNodeId);
        var scripts         = _trackingService.GetTrackingScripts(site.RootNodeId);
        var trackingScripts = scripts.BodyScripts;

        // Nav cascade: page-profile override → site footer nav → site root fallback
        var navNodeId = pageCfg.OverrideFooterNavNodeId
                     ?? footerCfg.FooterNavigationNodeId;

        var nav = navNodeId.HasValue
            ? _navigationService.GetFooterNavigation(navNodeId.Value)
            : _navigationService.GetFooterNavigation(site.RootNodeId);

        var hasContactInfo = !string.IsNullOrWhiteSpace(contactInfo.Email)
            || !string.IsNullOrWhiteSpace(contactInfo.Phone)
            || !string.IsNullOrWhiteSpace(contactInfo.Address);

        var model = new LayoutFooterViewModel(
            Brand: new LayoutBrandViewModel(
                HomeUrl:     layout.Brand.HomeUrl,
                SiteName:    layout.Brand.SiteName,
                SiteTagline: layout.Brand.SiteTagline,
                LogoUrl:     layout.Brand.LogoUrl,
                LogoAlt:     layout.Brand.LogoAlt),
            Theme: new LayoutThemeViewModel(
                ThemeColor:       layout.ThemeVariant,
                HeaderBackground: null,
                FooterBackground: null),
            NavigationItems:         MapNavigationItems(nav),
            FooterCopy:              footerCfg.CopyrightText,
            TrackingBodyScriptsHtml: trackingScripts,
            ApplicationRootName:     site.SiteName,
            NavigationRootName:      null,
            SocialLinks:             MapSocialLinks(footerCfg.SocialLinks),
            NewsletterActionUrl:     footerCfg.NewsletterActionUrl,
            ContactInfo:             hasContactInfo ? contactInfo : null);

        return Task.FromResult<IViewComponentResult>(View(model));
    }

    private static IReadOnlyList<SocialLinkViewModel> MapSocialLinks(
        IReadOnlyList<SocialLinkConfig> links)
        => links.Select(l => new SocialLinkViewModel(l.Label, l.Url, l.Platform)).ToList();

    private static IReadOnlyList<LayoutNavigationItemViewModel> MapNavigationItems(
        IReadOnlyList<NavigationItem> items)
        => items
            .Select(item => new LayoutNavigationItemViewModel(
                Title:    item.Title,
                Url:      item.Url,
                IsActive: item.IsActive,
                Children: MapNavigationItems(item.Children)))
            .ToList();
}
