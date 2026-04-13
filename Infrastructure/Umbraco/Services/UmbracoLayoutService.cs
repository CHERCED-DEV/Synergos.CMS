using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Aggregate layout service — implements <see cref="ILayoutService"/> by delegating
/// to seven focused, ISP-compliant service implementations.
///
/// Callers that only need one layout concern (e.g. only header or only SEO) should
/// inject the corresponding focused interface directly. This composite exists for
/// ViewComponents and controllers that need multiple concerns in a single dependency.
///
/// Focused services:
///   IThemeService    → UmbracoThemeService
///   IHeaderService   → UmbracoHeaderService
///   IFooterService   → UmbracoFooterService
///   IAlertBarService → UmbracoAlertBarService
///   IBannerService   → UmbracoBannerService
///   ITrackingService → UmbracoTrackingService
///   ISeoService      → UmbracoSeoService
/// </summary>
public sealed class UmbracoLayoutService : ILayoutService
{
    private readonly IThemeService    _theme;
    private readonly IHeaderService   _header;
    private readonly IFooterService   _footer;
    private readonly IAlertBarService _alertBar;
    private readonly IBannerService   _banner;
    private readonly ITrackingService _tracking;
    private readonly ISeoService      _seo;

    public UmbracoLayoutService(
        IThemeService    theme,
        IHeaderService   header,
        IFooterService   footer,
        IAlertBarService alertBar,
        IBannerService   banner,
        ITrackingService tracking,
        ISeoService      seo)
    {
        _theme    = theme;
        _header   = header;
        _footer   = footer;
        _alertBar = alertBar;
        _banner   = banner;
        _tracking = tracking;
        _seo      = seo;
    }

    // ── IThemeService ─────────────────────────────────────────────────────────
    public LayoutConfig     GetLayoutConfig(int rootNodeId)            => _theme.GetLayoutConfig(rootNodeId);
    public ThemeConfig?     GetThemeConfig(int rootNodeId)             => _theme.GetThemeConfig(rootNodeId);
    public PageLayoutConfig GetPageLayoutConfig(int? pageId)           => _theme.GetPageLayoutConfig(pageId);

    // ── IHeaderService ────────────────────────────────────────────────────────
    public SiteHeaderConfig GetSiteHeaderConfig(int rootNodeId)        => _header.GetSiteHeaderConfig(rootNodeId);

    // ── IFooterService ────────────────────────────────────────────────────────
    public SiteFooterConfig  GetSiteFooterConfig(int rootNodeId)       => _footer.GetSiteFooterConfig(rootNodeId);
    public ContactInfoConfig GetContactInfo(int rootNodeId)            => _footer.GetContactInfo(rootNodeId);

    // ── IAlertBarService ──────────────────────────────────────────────────────
    public AlertBarConfig?  GetAlertBarConfig(int rootNodeId)          => _alertBar.GetAlertBarConfig(rootNodeId);

    // ── IBannerService ────────────────────────────────────────────────────────
    public BannerConfig?    GetBannerConfig(int rootNodeId)            => _banner.GetBannerConfig(rootNodeId);

    // ── ITrackingService ──────────────────────────────────────────────────────
    public TrackingScriptsConfig GetTrackingScripts(int rootNodeId)    => _tracking.GetTrackingScripts(rootNodeId);

    // ── ISeoService ───────────────────────────────────────────────────────────
    public SeoDefaultsConfig GetSeoDefaults(int rootNodeId)            => _seo.GetSeoDefaults(rootNodeId);
}
