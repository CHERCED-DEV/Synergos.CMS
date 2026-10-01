using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IGlobalComponentResolver"/>. Resuelve la primera
/// pieza activa de cada tipo (alerta, banner, aviso footer, modal)
/// que aplique al request actual, buscando en TRES fuentes en orden
/// de prioridad:
/// </summary>
/// <remarks>
/// <para>
/// <strong>Prioridad 1 — Selector explícito en siteRoot (Ola 71):</strong>
/// el editor elige via ContentPicker (compTransversalSelectors:
/// activeAlertNode/activeBannerNode/activeFooterNoteNode/activeModalNode)
/// cuál nodo del repositorio está activo. Si el ContentPicker apunta
/// a un nodo válido + activo + en ventana programada, gana sin
/// consultar las otras fuentes.
/// </para>
/// <para>
/// <strong>Prioridad 2 — Repository scan (Ola 70):</strong>
/// si el selector explícito no aplica, busca el primer
/// <c>transversalAlert</c>/<c>transversalModal</c>/<c>transversalBanner</c>/
/// <c>transversalFooterNote</c> publicado activo + en ventana. Permite
/// auto-rotación de campañas sin tocar el siteRoot.
/// </para>
/// <para>
/// <strong>Prioridad 3 — Legacy BlockList (Olas 50 + 52):</strong>
/// fallback al <c>globalComponents</c> BlockList del primer
/// <c>siteConfigSettings</c> publicado. Backward compat con sites que
/// no migraron al repositorio.
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

    private const string SiteRootAlias = "siteRoot";
    private const string ActiveAlertNodeAlias = "activeAlertNode";
    private const string ActiveBannerNodeAlias = "activeBannerNode";
    private const string ActiveFooterNoteNodeAlias = "activeFooterNoteNode";
    private const string ActiveModalNodeAlias = "activeModalNode";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IVariationContextAccessor _variacion;
    private readonly IPublishedValueFallback _fallback;
    private readonly TimeProvider _reloj;

    /// <remarks>
    /// Los servicios de Umbraco llegan INYECTADOS y no por el localizador estático que usan las
    /// extensiones «amigables» (<c>Value&lt;T&gt;(alias)</c>, <c>DescendantsOrSelfOfType(alias)</c>):
    /// es lo mismo en ejecución, y es lo que deja probar el resolver sin arrancar Umbraco (#185).
    /// </remarks>
    public DefaultGlobalComponentResolver(
        IUmbracoContextAccessor umbracoContextAccessor,
        IVariationContextAccessor variacion,
        IPublishedValueFallback fallback,
        TimeProvider reloj)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _variacion = variacion;
        _fallback = fallback;
        _reloj = reloj;
    }

    public CfgAlert? GetActiveAlert()
    {
        if (IsSuppressed(SuppressAlertsAlias))
        {
            return null;
        }

        // Prioridad 1: selector explícito en siteRoot (Ola 71.9).
        var element = ResolveExplicitSelector(ActiveAlertNodeAlias, "alertActive", "alertScheduleStart", "alertScheduleEnd");
        // Prioridad 2: repository scan (Ola 70).
        element ??= FindActiveTransversal(TransversalAlertAlias, "alertActive", "alertScheduleStart", "alertScheduleEnd");
        // Prioridad 3: BlockList legacy (Olas 50 + 52).
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgAlertAlias, "alertActive", "alertScheduleStart", "alertScheduleEnd");
        }
        if (element is null)
        {
            return null;
        }

        var message = element.Value<string>(_fallback, "alertMessage");
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var ctaLink = element.Value<Link>(_fallback, "alertCtaLink");
        return new CfgAlert(
            Message: message,
            Variant: element.Value<string>(_fallback, "alertVariant"),
            Tone: element.Value<string>(_fallback, "alertTone"),
            Icon: element.Value<string>(_fallback, "alertIcon"),
            CtaLabel: element.Value<string>(_fallback, "alertCtaLabel"),
            CtaUrl: ctaLink?.Url,
            CtaOpenInNewTab: ctaLink is { Target: "_blank" },
            Dismissible: element.Value<bool>(_fallback, "alertDismissible"));
    }

    public CfgBanner? GetActiveBanner()
    {
        if (IsSuppressed(SuppressBannerAlias))
        {
            return null;
        }

        var element = ResolveExplicitSelector(ActiveBannerNodeAlias, "bannerActive", "bannerScheduleStart", "bannerScheduleEnd");
        element ??= FindActiveTransversal(TransversalBannerAlias, "bannerActive", "bannerScheduleStart", "bannerScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgBannerAlias, "bannerActive", "bannerScheduleStart", "bannerScheduleEnd");
        }
        if (element is null)
        {
            return null;
        }

        var message = element.Value<string>(_fallback, "bannerMessage");
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var ctaLink = element.Value<Link>(_fallback, "bannerCtaLink");
        var image = element.Value<MediaWithCrops>(_fallback, "bannerImage");
        var placement = element.Value<string>(_fallback, "bannerPlacement");
        return new CfgBanner(
            Message: message,
            ImageUrl: image?.Url(),
            CtaLabel: element.Value<string>(_fallback, "bannerCtaLabel"),
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

        var element = ResolveExplicitSelector(ActiveFooterNoteNodeAlias, "footerNoteActive", "footerNoteScheduleStart", "footerNoteScheduleEnd");
        element ??= FindActiveTransversal(TransversalFooterNoteAlias, "footerNoteActive", "footerNoteScheduleStart", "footerNoteScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgFooterNoteAlias, "footerNoteActive", "footerNoteScheduleStart", "footerNoteScheduleEnd");
        }
        if (element is null)
        {
            return null;
        }

        var text = element.Value<string>(_fallback, "footerNoteText");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var ctaLink = element.Value<Link>(_fallback, "footerNoteCtaLink");
        return new CfgFooterNote(
            Text: text,
            CtaLabel: element.Value<string>(_fallback, "footerNoteCtaLabel"),
            CtaUrl: ctaLink?.Url,
            CtaOpenInNewTab: ctaLink is { Target: "_blank" });
    }

    public CfgModal? GetActiveModal()
    {
        if (IsSuppressed(SuppressModalAlias))
        {
            return null;
        }

        var element = ResolveExplicitSelector(ActiveModalNodeAlias, "modalActive", "modalScheduleStart", "modalScheduleEnd");
        element ??= FindActiveTransversal(TransversalModalAlias, "modalActive", "modalScheduleStart", "modalScheduleEnd");
        if (element is null && TryGetGlobalComponentsBlockList(out var blocks))
        {
            element = FindActiveInBlockList(blocks, CfgModalAlias, "modalActive", "modalScheduleStart", "modalScheduleEnd");
        }
        if (element is null)
        {
            return null;
        }

        var title = element.Value<string>(_fallback, "modalTitle");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var ctaLink = element.Value<Link>(_fallback, "modalCtaLink");
        var image = element.Value<MediaWithCrops>(_fallback, "modalImage");
        var trigger = element.Value<string>(_fallback, "modalTrigger");
        var frequency = element.Value<string>(_fallback, "modalFrequency");
        return new CfgModal(
            Title: title,
            Body: element.Value<string>(_fallback, "modalBody"),
            ImageUrl: image?.Url(),
            CtaLabel: element.Value<string>(_fallback, "modalCtaLabel"),
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
        return page is not null && page.Value<bool>(_fallback, suppressAlias);
    }

    /// <summary>
    /// Prioridad 1: si el siteRoot tiene un ContentPicker apuntando a un
    /// nodo del repositorio (compTransversalSelectors), valida que el
    /// nodo esté activo + en ventana y lo devuelve. Si el picker está
    /// vacío, devuelve null para que el caller intente las otras
    /// fuentes.
    /// </summary>
    private IPublishedElement? ResolveExplicitSelector(
        string selectorAlias,
        string activeAlias,
        string scheduleStartAlias,
        string scheduleEndAlias)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            return null;
        }

        var page = umbracoContext.PublishedRequest?.PublishedContent;
        var siteRoot = page?.AncestorOrSelf(SiteRootAlias);
        if (siteRoot is null)
        {
            // Sin siteRoot ancestro (ej. PlatformRoot landing) — buscar el
            // primer siteRoot publicado para que el platform root también
            // pueda heredar selectores explícitos.
            siteRoot = umbracoContext.Content.GetAtRoot()
                .SelectMany(r => r.DescendantsOrSelfOfType(_variacion, SiteRootAlias))
                .FirstOrDefault();
            if (siteRoot is null)
            {
                return null;
            }
        }

        var picked = siteRoot.Value<IPublishedContent>(_fallback, selectorAlias);
        if (picked is null)
        {
            return null;
        }

        if (!picked.Value<bool>(_fallback, activeAlias))
        {
            return null;
        }

        return EnVentana(picked, scheduleStartAlias, scheduleEndAlias) ? picked : null;
    }

    /// <summary>
    /// Prioridad 2: busca el primer nodo Document publicado del tipo
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

        foreach (var root in umbracoContext.Content.GetAtRoot())
        {
            foreach (var node in root.DescendantsOrSelfOfType(_variacion, contentTypeAlias))
            {
                if (node.Value<bool>(_fallback, activeAlias)
                    && EnVentana(node, scheduleStartAlias, scheduleEndAlias))
                {
                    return node;
                }
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
            .SelectMany(root => root.DescendantsOrSelfOfType(_variacion, SiteConfigSettingsAlias))
            .FirstOrDefault();
        if (siteConfig is null)
        {
            return false;
        }

        var resolved = siteConfig.Value<BlockListModel>(_fallback, GlobalComponentsAlias);
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
    private IPublishedElement? FindActiveInBlockList(
        BlockListModel blocks,
        string contentTypeAlias,
        string activeAlias,
        string scheduleStartAlias,
        string scheduleEndAlias)
    {
        foreach (var item in blocks)
        {
            var element = item.Content;
            if (element is null || !string.Equals(element.ContentType.Alias, contentTypeAlias, StringComparison.Ordinal))
            {
                continue;
            }

            if (element.Value<bool>(_fallback, activeAlias)
                && EnVentana(element, scheduleStartAlias, scheduleEndAlias))
            {
                return element;
            }
        }
        return null;
    }

    /// <summary>
    /// Si el aviso está dentro de su ventana programada. Las dos puntas son OPCIONALES: sin
    /// inicio rige desde ya, y sin fin no vence (#185).
    /// </summary>
    /// <remarks>
    /// <para><b>Es UNA regla para las tres fuentes</b> —selector explícito, repositorio y
    /// BlockList legacy—, que antes la copiaban tres veces con el mismo defecto.</para>
    ///
    /// <para><b>El defecto.</b> Un aviso «hasta nuevo aviso» —lo más común que publica un
    /// editor— no se pintaba NUNCA: el fin vacío se comparaba como si fuera una fecha real, la
    /// del año 1, y el aviso salía vencido desde entonces. Sin error que lo dijera. Y tapaba el
    /// 500 latente de <c>_GlobalAlert</c>/<c>_GlobalBanner</c>/<c>_GlobalModal</c> (#184): como
    /// ningún aviso llegaba a pintarse, nadie compilaba esas vistas en caliente.</para>
    /// </remarks>
    private bool EnVentana(IPublishedElement aviso, string inicioAlias, string finAlias)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var inicio = Fecha(aviso, inicioAlias);
        var fin = Fecha(aviso, finAlias);
        return (inicio is null || ahora >= inicio.Value.ToUniversalTime())
            && (fin is null || ahora <= fin.Value.ToUniversalTime());
    }

    /// <summary>La fecha que el editor puso, o <c>null</c> si dejó el campo vacío.</summary>
    /// <remarks>
    /// Un campo de fecha vacío llega como el año 1, no como <c>null</c> (#184, #185); la regla es
    /// <see cref="FechasDelEditor"/>, una para todo el CMS (#188).
    /// </remarks>
    private DateTime? Fecha(IPublishedElement aviso, string alias) => aviso.FechaDelEditor(_fallback, alias);
}
