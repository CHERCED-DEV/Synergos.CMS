using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Constants;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IHostBridgeContextBuilder"/> que arma el shape
/// canónico de <c>window.synergos</c> consumiendo los seams existentes
/// (branding, theme, member gate, Umbraco context, localization).
/// </summary>
/// <remarks>
/// Ola 216, ADR 0083. Contract canónico documentado en
/// <c>docs/contracts/host-bridge.md</c>.
/// </remarks>
public sealed class DefaultHostBridgeContextBuilder : IHostBridgeContextBuilder
{
    private readonly IBrandingProvider _branding;
    private readonly IPageRenderContextResolver _renderCtx;
    private readonly IMemberAccessGate _gate;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly ILocalizationService _localizationService;
    private readonly IOptionsMonitor<HostBridgeSettings> _settings;
    private readonly ILogger<DefaultHostBridgeContextBuilder> _logger;

    public DefaultHostBridgeContextBuilder(
        IBrandingProvider branding,
        IPageRenderContextResolver renderCtx,
        IMemberAccessGate gate,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILocalizationService localizationService,
        IOptionsMonitor<HostBridgeSettings> settings,
        ILogger<DefaultHostBridgeContextBuilder> logger)
    {
        _branding = branding;
        _renderCtx = renderCtx;
        _gate = gate;
        _umbracoContextAccessor = umbracoContextAccessor;
        _localizationService = localizationService;
        _settings = settings;
        _logger = logger;
    }

    public HostBridgeContext Build()
    {
        var s = _settings.CurrentValue;
        var culture = CultureInfo.CurrentUICulture.Name;

        return new HostBridgeContext(
            Version: s.ContractVersion,
            I18n: BuildI18n(culture, s),
            Theme: BuildTheme(),
            Brand: BuildBrand(),
            Member: s.IncludeMemberContext ? BuildMember() : null,
            Page: s.IncludePageMetadata ? BuildPage() : EmptyPage());
    }

    private HostBridgeI18n BuildI18n(string culture, HostBridgeSettings s)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Enumerate root + descendants once. Filter by prefix subset.
            // ILocalizationService is scoped + cached internally por Umbraco.
            var roots = _localizationService.GetRootDictionaryItems();
            foreach (var root in roots)
            {
                CollectMatchingKeys(root.Key, root.ItemKey, culture, s.I18nKeyPrefixes, keys);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: la UI cae a sus strings inline y la página se sirve igual. Pero se
            // AVISA, que es lo único que faltaba (#92): éste es el más silencioso de los dos
            // catch del bridge, porque devuelve un contexto que PARECE sano —marca, tema y página
            // intactos— con el diccionario vacío. Sin esta línea, un sitio entero se queda sin
            // traducciones y no hay dónde verlo.
            _logger.LogWarning(ex,
                "No se pudieron leer las claves de diccionario para el host bridge (cultura {Cultura}). "
                + "La UI caerá a sus strings inline.", culture);
        }

        return new HostBridgeI18n(
            Culture: culture,
            DefaultCulture: "es-CO",
            Keys: keys);
    }

    private void CollectMatchingKeys(
        Guid id,
        string itemKey,
        string culture,
        string[] prefixes,
        Dictionary<string, string> output)
    {
        if (MatchesAnyPrefix(itemKey, prefixes))
        {
            var item = _localizationService.GetDictionaryItemByKey(itemKey);
            if (item is not null)
            {
                var translation = item.Translations.FirstOrDefault(t =>
                    string.Equals(t.LanguageIsoCode, culture, StringComparison.OrdinalIgnoreCase));
                if (translation is not null && !string.IsNullOrWhiteSpace(translation.Value))
                {
                    output[itemKey] = translation.Value;
                }
            }
        }

        foreach (var child in _localizationService.GetDictionaryItemDescendants(id))
        {
            // Solo level inmediato — el descendants ya itera el subtree.
            if (MatchesAnyPrefix(child.ItemKey, prefixes))
            {
                var translation = child.Translations.FirstOrDefault(t =>
                    string.Equals(t.LanguageIsoCode, culture, StringComparison.OrdinalIgnoreCase));
                if (translation is not null && !string.IsNullOrWhiteSpace(translation.Value))
                {
                    output[child.ItemKey] = translation.Value;
                }
            }
        }
    }

    private static bool MatchesAnyPrefix(string itemKey, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (itemKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private HostBridgeTheme BuildTheme()
    {
        var ctx = _renderCtx.Resolve();
        var variant = string.IsNullOrWhiteSpace(ctx.ThemeVariant)
            ? DropdownOptions.PageThemeVariant.Light
            : ctx.ThemeVariant;

        // La lista sale del mirror canónico, no de un literal. El literal
        // anterior decía ["light","dark","silvergold"]: tres variantes de las
        // ocho vigentes, y "silvergold" todo-minúscula — una ortografía que no
        // emite NADIE. `_Layout` escribe data-theme verbatim desde el editor,
        // o sea "silverGold" (ADR 0101 §2), así que un consumidor que hiciera
        // `available.includes(variant)` obtenía false para el tema activo, y
        // los cinco temas de los verticales no existían para la UI.
        return new HostBridgeTheme(
            Variant: variant,
            Available: DropdownOptions.PageThemeVariant.All);
    }

    private HostBridgeBrand BuildBrand()
    {
        var brand = _branding.GetCurrent();
        return new HostBridgeBrand(
            Key: brand.Key ?? "default",
            DisplayName: brand.DisplayName ?? "Synergos");
    }

    private HostBridgeMember? BuildMember()
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is null)
        {
            return null;
        }
        return new HostBridgeMember(
            Key: _gate.CurrentMemberKey.Value.ToString("N"),
            DisplayName: _gate.CurrentMemberDisplayName ?? string.Empty,
            Email: _gate.CurrentMemberEmail ?? string.Empty,
            Roles: _gate.CurrentMemberRoles.ToArray());
    }

    private HostBridgePage BuildPage()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx))
        {
            return EmptyPage();
        }

        var content = ctx.PublishedRequest?.PublishedContent;
        if (content is null)
        {
            return EmptyPage();
        }

        var canonical = content.Url(mode: Umbraco.Cms.Core.Models.PublishedContent.UrlMode.Absolute);
        var cultures = content.Cultures?.Keys?.ToArray() ?? Array.Empty<string>();

        return new HostBridgePage(
            Id: content.Id,
            DocType: content.ContentType?.Alias ?? string.Empty,
            CanonicalUrl: canonical ?? string.Empty,
            Cultures: cultures);
    }

    private static HostBridgePage EmptyPage() =>
        new(0, string.Empty, string.Empty, Array.Empty<string>());
}
