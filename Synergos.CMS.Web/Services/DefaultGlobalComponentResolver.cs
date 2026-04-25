using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IGlobalComponentResolver"/>. Lee el BlockList
/// <c>globalComponents</c> de <c>siteConfigSettings</c> (bajo
/// <c>settingsRoot</c>) y devuelve la primera pieza activa y dentro de
/// su ventana de visibilidad.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IUmbracoContextAccessor"/>. Nunca lanza: si no hay
/// request, no hay siteConfigSettings, ninguna pieza está activa o la
/// página suprime las alertas, devuelve <c>null</c>.
///
/// La resolución es perezosa por método: <c>GetActiveAlert</c> recorre
/// solo los items que sean <c>cfgAlert</c>; cuando se añadan
/// <c>cfgModal</c>/<c>cfgBanner</c>, sus métodos hermanos harán lo
/// equivalente sin tocar la lógica existente.
/// </remarks>
public sealed class DefaultGlobalComponentResolver : IGlobalComponentResolver
{
    private const string SiteConfigSettingsAlias = "siteConfigSettings";
    private const string CfgAlertAlias = "cfgAlert";
    private const string GlobalComponentsAlias = "globalComponents";
    private const string SuppressGlobalAlertsAlias = "suppressGlobalAlerts";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public DefaultGlobalComponentResolver(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public CfgAlert? GetActiveAlert()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return null;
        }

        var page = umbracoContext.PublishedRequest?.PublishedContent;
        if (page is not null && page.Value<bool>(SuppressGlobalAlertsAlias))
        {
            return null;
        }

        var siteConfig = ResolveSiteConfigSettings(umbracoContext);
        if (siteConfig is null)
        {
            return null;
        }

        var blocks = siteConfig.Value<BlockListModel>(GlobalComponentsAlias);
        if (blocks is null || blocks.Count == 0)
        {
            return null;
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var item in blocks)
        {
            var element = item.Content;
            if (element is null || !string.Equals(element.ContentType.Alias, CfgAlertAlias, StringComparison.Ordinal))
            {
                continue;
            }

            if (!element.Value<bool>("alertActive"))
            {
                continue;
            }

            var message = element.Value<string>("alertMessage");
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            var start = element.Value<DateTime?>("alertScheduleStart");
            if (start.HasValue && nowUtc < start.Value.ToUniversalTime())
            {
                continue;
            }

            var end = element.Value<DateTime?>("alertScheduleEnd");
            if (end.HasValue && nowUtc > end.Value.ToUniversalTime())
            {
                continue;
            }

            var ctaLink = element.Value<Link>("alertCtaLink");
            return new CfgAlert(
                Message: message,
                Variant: element.Value<string>("alertVariant"),
                Tone: element.Value<string>("alertTone"),
                Icon: element.Value<string>("alertIcon"),
                CtaLabel: element.Value<string>("alertCtaLabel"),
                CtaUrl: ctaLink?.Url,
                CtaOpenInNewTab: ctaLink is { Target: "_blank" },
                Dismissible: element.Value<bool>("alertDismissible"));
        }

        return null;
    }

    private static IPublishedContent? ResolveSiteConfigSettings(IUmbracoContext umbracoContext) =>
        umbracoContext.Content?
            .GetAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(SiteConfigSettingsAlias))
            .FirstOrDefault();
}
