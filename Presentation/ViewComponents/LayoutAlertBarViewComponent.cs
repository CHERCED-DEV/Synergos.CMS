using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Cdn.Configs;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Services;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Presentation.ViewComponents;

public sealed class LayoutAlertBarViewComponent : ViewComponent
{
    private readonly ISiteResolver           _siteResolver;
    private readonly IThemeService           _themeService;
    private readonly IAlertBarService        _alertBarService;
    private readonly IElementUrlResolver      _elementUrl;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public LayoutAlertBarViewComponent(
        ISiteResolver           siteResolver,
        IThemeService           themeService,
        IAlertBarService        alertBarService,
        IElementUrlResolver      elementUrl,
        IUmbracoContextAccessor umbracoContextAccessor)
    {
        _siteResolver           = siteResolver;
        _themeService           = themeService;
        _alertBarService        = alertBarService;
        _elementUrl             = elementUrl;
        _umbracoContextAccessor = umbracoContextAccessor;
    }

    public IViewComponentResult Invoke()
    {
        var site = _siteResolver.Resolve();

        // Respect per-page layout profile: if this page suppresses the alert bar, skip it.
        _umbracoContextAccessor.TryGetUmbracoContext(out var ctx);
        var pageCfg = _themeService.GetPageLayoutConfig(ctx?.PublishedRequest?.PublishedContent?.Id);
        if (!pageCfg.ShowAlertBar) return Content(string.Empty);

        var cfg = _alertBarService.GetAlertBarConfig(site.RootNodeId);
        if (cfg is null) return Content(string.Empty);

        var alertCfg = new AlertBarCdnConfig(
            Title:       cfg.Title,
            Description: cfg.Description,
            CtaLabel:    cfg.CtaLabel,
            CtaUrl:      cfg.CtaUrl,
            Variant:     cfg.Variant,
            Dismissible: cfg.Dismissible ? true : null);

        var opt = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy   = JsonNamingPolicy.CamelCase
        };

        ViewData["AlertBarConfig"]    = JsonSerializer.Serialize(alertCfg, opt);
        ViewData["AlertBarBundleUrl"] = _elementUrl.ResolveBundle("alert-bar");
        return View();
    }
}
