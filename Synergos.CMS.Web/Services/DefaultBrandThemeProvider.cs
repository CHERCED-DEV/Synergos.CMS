using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IBrandThemeProvider"/> that reads brand theme
/// nodes from the Umbraco content tree by PROPERTY (any node with a
/// <c>primaryColor</c> property — i.e. <c>themeSettings</c> OR the newer
/// <c>siteConfiguration</c> that composes it). Resolves the first match
/// by <c>brandKey</c>. (P0-4: antes matcheaba solo el alias legacy
/// <c>themeSettings</c>, dejando sin tema a sites que siguen la guía.)
/// </summary>
/// <remarks>
/// Lives in <c>Synergos.CMS.Web</c> because it depends on
/// <see cref="IUmbracoContextAccessor"/>. Umbraco types do not leak
/// into <c>Synergos.CMS.Application</c> (ADR 0002). The provider
/// projects the Umbraco node into the neutral
/// <see cref="BrandTheme"/> record.
/// </remarks>
public sealed class DefaultBrandThemeProvider : IBrandThemeProvider
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public DefaultBrandThemeProvider(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public BrandTheme? GetThemeForBrand(string brandKey)
    {
        if (string.IsNullOrWhiteSpace(brandKey))
        {
            return null;
        }

        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return null;
        }

        // P0-4: matchear por PROPIEDAD (no por alias de content type). El alias
        // `themeSettings` quedó legacy (Ola 71); la guía empuja a crear un nodo
        // `siteConfiguration` que COMPONE themeSettings (alias distinto). Buscar
        // por HasProperty("primaryColor") encuentra ambos — un site que sigue la
        // doc ya no se queda sin tema de marca.
        var themeNode = umbracoContext.Content?
            .GetAtRoot()
            .SelectMany(root => root.DescendantsOrSelf())
            .FirstOrDefault(node =>
                node.HasProperty("primaryColor") &&
                string.Equals(node.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (themeNode is null)
        {
            return null;
        }

        return new BrandTheme(
            PrimaryColor: themeNode.Value<string>("primaryColor"),
            SecondaryColor: themeNode.Value<string>("secondaryColor"),
            AccentColor: themeNode.Value<string>("accentColor"),
            BackgroundColor: themeNode.Value<string>("backgroundColor"),
            TextColor: themeNode.Value<string>("textColor"),
            HeadingFontFamily: themeNode.Value<string>("headingFontFamily"),
            BodyFontFamily: themeNode.Value<string>("bodyFontFamily"),
            LogoLightUrl: themeNode.Value<IPublishedContent>("logoLight")?.Url(),
            LogoDarkUrl: themeNode.Value<IPublishedContent>("logoDark")?.Url(),
            FaviconUrl: themeNode.Value<IPublishedContent>("favicon")?.Url());
    }
}
