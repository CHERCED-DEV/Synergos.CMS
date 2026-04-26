using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IGlobalComponentResolver"/>. Resuelve la primera
/// pieza activa de cada tipo (alerta, banner, aviso footer, modal)
/// que aplique al request actual, buscando en dos fuentes en orden
/// de prioridad:
/// </summary>
/// <remarks>
/// <para>
/// <strong>Prioridad 1 — Transversales repository (Ola 70):</strong>
/// busca <c>transversalAlert</c>/<c>transversalModal</c>/<c>transversalBanner</c>/
/// <c>transversalFooterNote</c> publicados (Document types que componen
/// los <c>cfg*</c>, así heredan los mismos aliases de propiedades).
/// El primer nodo activo + dentro de su ventana programada gana.
/// </para>
/// <para>
/// <strong>Prioridad 2 — Legacy BlockList (Olas 50 + 52):</strong>
/// fallback al <c>globalComponents</c> BlockList del primer
/// <c>siteConfigSettings</c> publicado.
/// </para>
/// <para>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IUmbracoContextAccessor"/>. Nunca lanza: si no hay
/// request, no hay matches o la página suprime el componente,
/// devuelve <c>null</c>.
/// </para>
/// </remarks>
public sealed class DefaultGlobalComponentResolver : IGlobalComponentResolver
{
    private const string SiteConfigSettingsAlias = "siteConfigSettings";
    private const string GlobalComponentsAlias = "globalComponents";

    private const string CfgAlertAlias = "cfgAlert";
    private const string CfgBannerAlias = "cfgBanner";
    private const string CfgFooterNoteAlias = "cfgFooterNote";
    private const string CfgModalAlias = "cfgModal";

    private const string TransversalAlertAlias = "transversalAlert";
    private const string TransversalBannerAlias = "transversalBanner";
    private const string TransversalFooterNoteAlias = "transversalFooterNote";
    private const string TransversalModalAlias = "transversalModal";

    private const string SuppressAlertsAlias = "suppressGlobalAlerts";
    private const string SuppressBannerAlias = "suppressGlobalBanner";
    private const string SuppressFooterNoteAlias = "suppressGlobalFooterNote";
    private const string SuppressModalAlias = "suppressGlobalModal";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public DefaultGlobalComponentResolver(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public CfgAlert? GetActiveAlert()
    {
        if (IsSuppressed(SuppressAlertsAlias))
        {
            return null;
        }

        var element = FindActiveTransversal(TransversalAlertAlias, "alertActive", "alertScheduleStart", "alertScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgAlertAlias, "alertActive", "alertScheduleStart", "alertScheduleEnd");
        }
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
        if (IsSuppressed(SuppressBannerAlias))
        {
            return null;
        }

        var element = FindActiveTransversal(TransversalBannerAlias, "bannerActive", "bannerScheduleStart", "bannerScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgBannerAlias, "bannerActive", "bannerScheduleStart", "bannerScheduleEnd");
        }
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
        if (IsSuppressed(SuppressFooterNoteAlias))
        {
            return null;
        }

        var element = FindActiveTransversal(TransversalFooterNoteAlias, "footerNoteActive", "footerNoteScheduleStart", "footerNoteScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgFooterNoteAlias, "footerNoteActive", "footerNoteScheduleStart", "footerNoteScheduleEnd");
        }
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
        if (IsSuppressed(SuppressModalAlias))
        {
            return null;
        }

        var element = FindActiveTransversal(TransversalModalAlias, "modalActive", "modalScheduleStart", "modalScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgModalAlias, "modalActive", "modalScheduleStart", "modalScheduleEnd");
        }
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
    /// Devuelve true si la página actual suprime el componente vía la
    /// flag de compPageOrchestration correspondiente. true también si
    /// no hay request (no hay nada que renderizar).
    /// </summary>
    private bool IsSuppressed(string suppressAlias)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return true;
        }
        var page = umbracoContext.PublishedRequest?.PublishedContent;
        return page is not null && page.Value<bool>(suppressAlias);
    }

    /// <summary>
    /// Prioridad 1: busca el primer nodo Document publicado del tipo
    /// transversal indicado que esté activo + dentro de su ventana
    /// programada. Funciona porque los transversal* componen los cfg*
    /// y heredan los mismos alias de propiedades.
    /// </summary>
    private IPublishedElement? FindActiveTransversal(
        string contentTypeAlias,
        string activeAlias,
        string scheduleStartAlias,
        string scheduleEndAlias)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            return null;
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var root in umbracoContext.Content.GetAtRoot())
        {
            foreach (var node in root.DescendantsOrSelfOfType(contentTypeAlias))
            {
                if (!node.Value<bool>(activeAlias))
                {
                    continue;
                }

                var start = node.Value<DateTime?>(scheduleStartAlias);
                if (start.HasValue && nowUtc < start.Value.ToUniversalTime())
                {
                    continue;
                }

                var end = node.Value<DateTime?>(scheduleEndAlias);
                if (end.HasValue && nowUtc > end.Value.ToUniversalTime())
                {
                    continue;
                }

                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Prioridad 2 (legacy): obtiene el BlockList globalComponents del
    /// primer siteConfigSettings publicado. Devuelve false si no hay
    /// siteConfigSettings o si el BlockList está vacío.
    /// </summary>
    private bool TryGetGlobalComponentsBlockList(out BlockListModel blocks)
    {
        blocks = null!;
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
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

        var resolved = siteConfig.Value<BlockListModel>(GlobalComponentsAlias);
        if (resolved is null || resolved.Count == 0)
        {
            return false;
        }

        blocks = resolved;
        return true;
    }

    /// <summary>
    /// Prioridad 2 helper: busca el primer Element del tipo cfg* en el
    /// BlockList que esté activo + dentro de su ventana programada.
    /// </summary>
    private static IPublishedElement? FindActiveInBlockList(
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
