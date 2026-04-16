using Synergos.CMS.Application;
using Umbraco.Cms.Core.Models;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves site header configuration.
///
/// v11.0.0 — reads from the active Layout Profile's HeaderConfig picker when
/// available (<c>&lt;site&gt;/Config/Layout/Header/…</c>). Falls back to legacy
/// SiteSettings tabs when no profile is configured yet (transitional through F2→F3).
///
/// Nav cascade for legacy path: SiteSettings.headerNavigation → BlogHome.blogNavigation.
/// </summary>
public sealed class UmbracoHeaderService : IHeaderService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoHeaderService(IContentContextAccessor accessor) => _accessor = accessor;

    public SiteHeaderConfig GetSiteHeaderConfig(int rootNodeId)
    {
        // Primary path — resolve via Layout Profile → HeaderConfig node.
        var profile      = LayoutContentResolver.ResolveLayoutProfile(_accessor, rootNodeId);
        var headerConfig = profile is null
            ? null
            : LayoutContentResolver.ReadPickerContent(profile, "headerConfig");

        if (headerConfig is not null &&
            headerConfig.ContentType.Alias == ContentTypeKeys.Aliases.HeaderConfig)
        {
            var navPick = LayoutContentResolver.ReadPickerContent(headerConfig, "headerNavigation");
            var showCta = headerConfig.Value<bool>("showCta");
            string? ctaLabel = null;
            string? ctaUrl   = null;

            if (showCta)
            {
                ctaLabel = headerConfig.Value<string>("ctaLabel");
                ctaUrl   = headerConfig.Value<IEnumerable<Link>>("ctaUrl")?.FirstOrDefault()?.Url;
            }

            return new SiteHeaderConfig(
                NavigationNodeId: navPick?.Id,
                ShowCta:          showCta,
                CtaLabel:         ctaLabel,
                CtaUrl:           ctaUrl);
        }

        // Legacy fallback — SiteSettings tabs (pre-v11 data).
        var root         = _accessor.GetById(rootNodeId);
        var siteSettings = root?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);

        if (siteSettings is null)
            return new SiteHeaderConfig(null, false, null, null);

        var legacyNav = LayoutContentResolver.ReadPickerContent(siteSettings, "headerNavigation");

        // Blog nav fallback when no main nav is configured.
        if (legacyNav is null)
        {
            var blogHome = root?.Children.FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.BlogHome);
            if (blogHome is not null)
                legacyNav = LayoutContentResolver.ReadPickerContent(blogHome, "blogNavigation");
        }

        var legacyShowCta = siteSettings.Value<bool>("showHeaderCta");
        string? legacyCtaLabel = null;
        string? legacyCtaUrl   = null;

        if (legacyShowCta)
        {
            legacyCtaLabel = siteSettings.Value<string>("headerCtaLabel");
            legacyCtaUrl   = siteSettings.Value<IEnumerable<Link>>("headerCtaUrl")?.FirstOrDefault()?.Url;
        }

        return new SiteHeaderConfig(
            NavigationNodeId: legacyNav?.Id,
            ShowCta:          legacyShowCta,
            CtaLabel:         legacyCtaLabel,
            CtaUrl:           legacyCtaUrl);
    }
}
