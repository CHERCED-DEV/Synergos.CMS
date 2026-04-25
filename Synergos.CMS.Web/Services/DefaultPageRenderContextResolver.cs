using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPageRenderContextResolver"/> que aplica la
/// cascada page → siteRoot → defaults inline (ADR 0022) leyendo las
/// propiedades de las composiciones <c>compPageOrchestration</c> y
/// <c>compPageTheme</c> sobre la página resuelta y su siteRoot
/// ancestral.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IUmbracoContextAccessor"/>. Convierte las decisiones del
/// editor en flags booleanas para que las plantillas Razor no repitan la
/// misma lógica de string-matching en cada vista. Nunca lanza: si no hay
/// request o página, devuelve <see cref="PageRenderContext.Defaults"/>.
/// Componentes globales (alertas/modales/banners) se resuelven aparte
/// vía <c>IGlobalComponentResolver</c> — no son responsabilidad de este
/// resolver.
/// </remarks>
public sealed class DefaultPageRenderContextResolver : IPageRenderContextResolver
{
    private const string SiteRootAlias = "siteRoot";
    private const string InheritValue = "inherit";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public DefaultPageRenderContextResolver(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public PageRenderContext Resolve()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return PageRenderContext.Defaults();
        }

        var page = umbracoContext.PublishedRequest?.PublishedContent;
        if (page is null)
        {
            return PageRenderContext.Defaults();
        }

        var siteRoot = page.AncestorOrSelf(SiteRootAlias);
        var defaults = PageRenderContext.Defaults();

        var chromeMode = ResolveString(page, siteRoot, "chromeMode", defaults.ChromeMode);
        var headerMode = ResolveString(page, siteRoot, "headerMode", defaults.HeaderMode);
        var footerMode = ResolveString(page, siteRoot, "footerMode", defaults.FooterMode);

        var showHeader = ChromeAllowsChrome(chromeMode) && !string.Equals(headerMode, "hidden", StringComparison.OrdinalIgnoreCase);
        var showFooter = ChromeAllowsChrome(chromeMode) && !string.Equals(footerMode, "hidden", StringComparison.OrdinalIgnoreCase);

        var showTitle = ResolveBool(page, siteRoot, "showTitle", defaults.ShowTitle) && ChromeAllowsContent(chromeMode);
        var showBreadcrumbs = ResolveBool(page, siteRoot, "showBreadcrumbs", defaults.ShowBreadcrumbs) && ChromeAllowsChrome(chromeMode);

        return new PageRenderContext(
            ChromeMode: chromeMode,
            HeaderMode: headerMode,
            FooterMode: footerMode,
            ShowHeader: showHeader,
            ShowFooter: showFooter,
            ShowTitle: showTitle,
            ShowBreadcrumbs: showBreadcrumbs,
            ThemeVariant: ResolveString(page, siteRoot, "pageThemeVariant", defaults.ThemeVariant),
            PageSurface: ResolveString(page, siteRoot, "pageSurface", defaults.PageSurface),
            VisualProfile: ResolveString(page, siteRoot, "visualProfile", defaults.VisualProfile),
            ContainerType: ResolveString(page, siteRoot, "pageContainerType", defaults.ContainerType),
            SpacingScale: ResolveString(page, siteRoot, "pageSpacingScale", defaults.SpacingScale));
    }

    private static string ResolveString(IPublishedContent page, IPublishedContent? siteRoot, string alias, string fallback)
    {
        var pageValue = page.Value<string>(alias);
        if (!string.IsNullOrWhiteSpace(pageValue) && !string.Equals(pageValue, InheritValue, StringComparison.OrdinalIgnoreCase))
        {
            return pageValue;
        }

        var rootValue = siteRoot?.Value<string>(alias);
        if (!string.IsNullOrWhiteSpace(rootValue) && !string.Equals(rootValue, InheritValue, StringComparison.OrdinalIgnoreCase))
        {
            return rootValue;
        }

        return fallback;
    }

    private static bool ResolveBool(IPublishedContent page, IPublishedContent? siteRoot, string alias, bool fallback)
    {
        if (page.HasValue(alias))
        {
            return page.Value(alias, defaultValue: fallback);
        }

        if (siteRoot is not null && siteRoot.HasValue(alias))
        {
            return siteRoot.Value(alias, defaultValue: fallback);
        }

        return fallback;
    }

    private static bool ChromeAllowsChrome(string chromeMode) =>
        !string.Equals(chromeMode, "none", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(chromeMode, "bare", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(chromeMode, "embedded", StringComparison.OrdinalIgnoreCase);

    private static bool ChromeAllowsContent(string chromeMode) =>
        !string.Equals(chromeMode, "none", StringComparison.OrdinalIgnoreCase);
}
