using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Presentation.ViewModels.Layout;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Presentation.ViewComponents;

public sealed class LayoutHeaderViewComponent : ViewComponent
{
    private readonly ISiteResolver           _siteResolver;
    private readonly INavigationService      _navigationService;
    private readonly IThemeService           _themeService;
    private readonly IHeaderService          _headerService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public LayoutHeaderViewComponent(
        ISiteResolver           siteResolver,
        INavigationService      navigationService,
        IThemeService           themeService,
        IHeaderService          headerService,
        IUmbracoContextAccessor umbracoContextAccessor)
    {
        _siteResolver           = siteResolver;
        _navigationService      = navigationService;
        _themeService           = themeService;
        _headerService          = headerService;
        _umbracoContextAccessor = umbracoContextAccessor;
    }

    public Task<IViewComponentResult> InvokeAsync()
    {
        var site = _siteResolver.Resolve();

        // Per-page layout config (from CompDomLayoutProfile composition on the page)
        _umbracoContextAccessor.TryGetUmbracoContext(out var ctx);
        var currentPage = ctx?.PublishedRequest?.PublishedContent;
        var pageCfg     = _themeService.GetPageLayoutConfig(currentPage?.Id);

        if (!pageCfg.ShowHeader)
            return Task.FromResult<IViewComponentResult>(Content(string.Empty));

        var layout    = _themeService.GetLayoutConfig(site.RootNodeId);
        var headerCfg = _headerService.GetSiteHeaderConfig(site.RootNodeId);

        // Nav cascade: page-profile override → site header nav → site root fallback
        var navNodeId = pageCfg.OverrideHeaderNavNodeId
                     ?? headerCfg.NavigationNodeId;

        var nav = navNodeId.HasValue
            ? _navigationService.GetMainNavigation(navNodeId.Value)
            : _navigationService.GetMainNavigation(site.RootNodeId);

        var cultureOptions = _siteResolver.GetCultureOptions(site.Culture)
            .Select(o => new CultureOptionViewModel(o.Label, o.Culture, o.Url, o.IsActive))
            .ToList();

        var model = new LayoutHeaderViewModel(
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
            NavigationItems:     MapNavigationItems(nav),
            CultureOptions:      cultureOptions,
            ApplicationRootName: site.SiteName,
            NavigationRootName:  null,
            CtaLabel:            headerCfg.CtaLabel,
            CtaUrl:              headerCfg.CtaUrl);

        return Task.FromResult<IViewComponentResult>(View(model));
    }

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
