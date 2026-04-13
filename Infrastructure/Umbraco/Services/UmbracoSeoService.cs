using Synergos.CMS.Application;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves site-level SEO default values for use as page-level fallbacks.
///
/// Cascade:
///   SiteSettings.seoDefaultDescription → GlobalSettings.defaultSeoDescription
///   SiteSettings.seoDefaultOgImage     → GlobalSettings.defaultOgImage
///   SiteSettings.seoTitleSuffix        (no global fallback)
/// </summary>
public sealed class UmbracoSeoService : ISeoService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoSeoService(IContentContextAccessor accessor) => _accessor = accessor;

    public SeoDefaultsConfig GetSeoDefaults(int rootNodeId)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null) return new SeoDefaultsConfig(null, null, null);

        var description = ss.Value<string>("seoDefaultDescription");
        var ogImageUrl  = LayoutContentResolver.ReadMediaUrl(ss, "seoDefaultOgImage");

        if (string.IsNullOrWhiteSpace(description) || ogImageUrl is null)
        {
            var gs = SiteSettingsAccessor.ResolveGlobalSettings(_accessor, rootNodeId);
            if (gs is not null)
            {
                if (string.IsNullOrWhiteSpace(description))
                    description = gs.Value<string>("defaultSeoDescription");
                ogImageUrl ??= LayoutContentResolver.ReadMediaUrl(gs, "defaultOgImage");
            }
        }

        return new SeoDefaultsConfig(
            TitleSuffix:        ss.Value<string>("seoTitleSuffix"),
            DefaultDescription: description,
            DefaultOgImageUrl:  ogImageUrl);
    }
}
