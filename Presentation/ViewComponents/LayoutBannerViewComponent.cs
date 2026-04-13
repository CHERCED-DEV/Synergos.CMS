using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Web;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Presentation.ViewComponents;

/// <summary>
/// Renders the global promotional banner configured in SiteSettings → Banners &amp; Campaigns.
/// The banner is a ReusableBlock whose Block Grid content is rendered below the header.
/// Respects globalBannerEnabled toggle and optional campaignStart/End date window.
/// </summary>
public sealed class LayoutBannerViewComponent : ViewComponent
{
    private readonly ISiteResolver           _siteResolver;
    private readonly IThemeService           _themeService;
    private readonly IBannerService          _bannerService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public LayoutBannerViewComponent(
        ISiteResolver           siteResolver,
        IThemeService           themeService,
        IBannerService          bannerService,
        IUmbracoContextAccessor umbracoContextAccessor)
    {
        _siteResolver           = siteResolver;
        _themeService           = themeService;
        _bannerService          = bannerService;
        _umbracoContextAccessor = umbracoContextAccessor;
    }

    public IViewComponentResult Invoke()
    {
        var site = _siteResolver.Resolve();

        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx))
            return Content(string.Empty);

        // Respect per-page layout profile: if this page suppresses the banner, skip it.
        var pageCfg = _themeService.GetPageLayoutConfig(ctx.PublishedRequest?.PublishedContent?.Id);
        if (!pageCfg.ShowBanner) return Content(string.Empty);

        var bannerCfg = _bannerService.GetBannerConfig(site.RootNodeId);
        if (bannerCfg is null) return Content(string.Empty);

        var bannerNode = ctx.Content?.GetById(bannerCfg.BannerNodeId);
        if (bannerNode is null) return Content(string.Empty);

        return View(bannerNode);
    }
}
