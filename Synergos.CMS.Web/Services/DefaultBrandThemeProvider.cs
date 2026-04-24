using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IBrandThemeProvider"/> that reads
/// <c>themeSettings</c> nodes from the Umbraco content tree (Ola 31
/// schema — ADR 0020). Resolves the first match by <c>brandKey</c>
/// under the Settings tree.
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
    private const string ThemeSettingsAlias = "themeSettings";

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

        var themeNode = umbracoContext.Content?
            .GetAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(ThemeSettingsAlias))
            .FirstOrDefault(node => string.Equals(
                node.Value<string>("brandKey"),
                brandKey,
                StringComparison.OrdinalIgnoreCase));

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
