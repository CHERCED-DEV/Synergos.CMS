using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IGlobalComponentResolver"/>. Lee el BlockList
/// <c>globalComponents</c> de <c>siteConfigSettings</c> y devuelve la
/// primera pieza activa de cada tipo (alerta, banner, aviso footer,
/// modal) que aplique al request actual.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IUmbracoContextAccessor"/>. Nunca lanza: si no hay
/// request, no hay siteConfigSettings, ninguna pieza está activa o la
/// página suprime el componente, devuelve <c>null</c>.
///
/// Cada método busca su propio cfg* en el mismo BlockList; el resolver
/// no comparte estado entre métodos. Esto permite que cada cfg* tenga
/// su propia regla (ej. modal con frequency/trigger en cliente).
/// </remarks>
public sealed class DefaultGlobalComponentResolver : IGlobalComponentResolver
{
    private const string SiteConfigSettingsAlias = "siteConfigSettings";
    private const string GlobalComponentsAlias = "globalComponents";

    private const string CfgAlertAlias = "cfgAlert";
    private const string CfgBannerAlias = "cfgBanner";
    private const string CfgFooterNoteAlias = "cfgFooterNote";
    private const string CfgModalAlias = "cfgModal";

    private const string SuppressAlertsAlias = "suppressGlobalAlerts";
    private const string SuppressBannerAlias = "suppressGlobalBanner";
    private const string SuppressFooterNoteAlias = "suppressGlobalFooterNote";
    private const string SuppressModalAlias = "suppressGlobalModal";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public DefaultGlobalComponentResolver(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public CfgAlert? GetActiveAlert()
    {
        if (!TryResolve(SuppressAlertsAlias, out var blocks))
        {
            return null;
        }

        var element = FindActiveScheduled(blocks!, CfgAlertAlias, "alertActive", "alertScheduleStart", "alertScheduleEnd");
        if (element is null)
        {
            return null;
        }

        var message = element.Value<string>("alertMessage");
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
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

    public CfgBanner? GetActiveBanner()
    {
        if (!TryResolve(SuppressBannerAlias, out var blocks))
        {
            return null;
        }

        var element = FindActiveScheduled(blocks!, CfgBannerAlias, "bannerActive", "bannerScheduleStart", "bannerScheduleEnd");
        if (element is null)
        {
            return null;
        }

        var message = element.Value<string>("bannerMessage");
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var ctaLink = element.Value<Link>("bannerCtaLink");
        var image = element.Value<MediaWithCrops>("bannerImage");
        var placement = element.Value<string>("bannerPlacement");
        return new CfgBanner(
            Message: message,
            ImageUrl: image?.Url(),
            CtaLabel: element.Value<string>("bannerCtaLabel"),
            CtaUrl: ctaLink?.Url,
            CtaOpenInNewTab: ctaLink is { Target: "_blank" },
            Placement: string.IsNullOrWhiteSpace(placement) ? "top" : placement);
    }

    public CfgFooterNote? GetActiveFooterNote()
    {
        if (!TryResolve(SuppressFooterNoteAlias, out var blocks))
        {
            return null;
        }

        var element = FindActiveScheduled(blocks!, CfgFooterNoteAlias, "footerNoteActive", "footerNoteScheduleStart", "footerNoteScheduleEnd");
        if (element is null)
        {
            return null;
        }

        var text = element.Value<string>("footerNoteText");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var ctaLink = element.Value<Link>("footerNoteCtaLink");
        return new CfgFooterNote(
            Text: text,
            CtaLabel: element.Value<string>("footerNoteCtaLabel"),
            CtaUrl: ctaLink?.Url,
            CtaOpenInNewTab: ctaLink is { Target: "_blank" });
    }

    public CfgModal? GetActiveModal()
    {
        if (!TryResolve(SuppressModalAlias, out var blocks))
        {
            return null;
        }

        var element = FindActiveScheduled(blocks!, CfgModalAlias, "modalActive", "modalScheduleStart", "modalScheduleEnd");
        if (element is null)
        {
            return null;
        }

        var title = element.Value<string>("modalTitle");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var ctaLink = element.Value<Link>("modalCtaLink");
        var image = element.Value<MediaWithCrops>("modalImage");
        var trigger = element.Value<string>("modalTrigger");
        var frequency = element.Value<string>("modalFrequency");
        return new CfgModal(
            Title: title,
            Body: element.Value<string>("modalBody"),
            ImageUrl: image?.Url(),
            CtaLabel: element.Value<string>("modalCtaLabel"),
            CtaUrl: ctaLink?.Url,
            CtaOpenInNewTab: ctaLink is { Target: "_blank" },
            Trigger: string.IsNullOrWhiteSpace(trigger) ? "immediate" : trigger,
            Frequency: string.IsNullOrWhiteSpace(frequency) ? "always" : frequency);
    }

    /// <summary>
    /// Resuelve los blocks del BlockList globalComponents respetando la
    /// flag de suppress por página correspondiente. Devuelve false si
    /// no hay request, no hay siteConfigSettings, está suprimida o el
    /// BlockList está vacío.
    /// </summary>
    private bool TryResolve(string suppressAlias, out BlockListModel? blocks)
    {
        blocks = null;
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return false;
        }

        var page = umbracoContext.PublishedRequest?.PublishedContent;
        if (page is not null && page.Value<bool>(suppressAlias))
        {
            return false;
        }

        var siteConfig = umbracoContext.Content?
            .GetAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(SiteConfigSettingsAlias))
            .FirstOrDefault();
        if (siteConfig is null)
        {
            return false;
        }

        blocks = siteConfig.Value<BlockListModel>(GlobalComponentsAlias);
        return blocks is { Count: > 0 };
    }

    /// <summary>
    /// Busca el primer IPublishedElement del tipo indicado que esté
    /// activo y dentro de su ventana de fechas (si tiene). Devuelve
    /// null si ninguno aplica.
    /// </summary>
    private static IPublishedElement? FindActiveScheduled(
        BlockListModel blocks,
        string contentTypeAlias,
        string activeAlias,
        string scheduleStartAlias,
        string scheduleEndAlias)
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var item in blocks)
        {
            var element = item.Content;
            if (element is null || !string.Equals(element.ContentType.Alias, contentTypeAlias, StringComparison.Ordinal))
            {
                continue;
            }

            if (!element.Value<bool>(activeAlias))
            {
                continue;
            }

            var start = element.Value<DateTime?>(scheduleStartAlias);
            if (start.HasValue && nowUtc < start.Value.ToUniversalTime())
            {
                continue;
            }

            var end = element.Value<DateTime?>(scheduleEndAlias);
            if (end.HasValue && nowUtc > end.Value.ToUniversalTime())
            {
                continue;
            }

            return element;
        }

        return null;
    }
}
