using Synergos.CMS.Application;
using Umbraco.Cms.Core.Models;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves site header configuration from SiteSettings.
///
/// Responsibilities:
///   - GetSiteHeaderConfig — header nav node ID, CTA visibility and URL
///
/// Nav cascade: SiteSettings.headerNavigation → BlogHome.blogNavigation (fallback when
/// the current site hosts a blog and no main nav is configured on SiteSettings).
/// </summary>
public sealed class UmbracoHeaderService : IHeaderService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoHeaderService(IContentContextAccessor accessor) => _accessor = accessor;

    public SiteHeaderConfig GetSiteHeaderConfig(int rootNodeId)
    {
        var root         = _accessor.GetById(rootNodeId);
        var siteSettings = root?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);

        if (siteSettings is null)
            return new SiteHeaderConfig(null, false, null, null);

        var navPick = LayoutContentResolver.ReadPickerContent(siteSettings, "headerNavigation");

        // Fallback: if no main nav configured, check BlogHome for its own nav.
        if (navPick is null)
        {
            var blogHome = root?.Children.FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.BlogHome);
            if (blogHome is not null)
                navPick = LayoutContentResolver.ReadPickerContent(blogHome, "blogNavigation");
        }

        var showCta  = siteSettings.Value<bool>("showHeaderCta");
        string? ctaLabel = null;
        string? ctaUrl   = null;

        if (showCta)
        {
            ctaLabel = siteSettings.Value<string>("headerCtaLabel");
            ctaUrl   = siteSettings.Value<IEnumerable<Link>>("headerCtaUrl")?.FirstOrDefault()?.Url;
        }

        return new SiteHeaderConfig(
            NavigationNodeId: navPick?.Id,
            ShowCta:          showCta,
            CtaLabel:         ctaLabel,
            CtaUrl:           ctaUrl);
    }
}
