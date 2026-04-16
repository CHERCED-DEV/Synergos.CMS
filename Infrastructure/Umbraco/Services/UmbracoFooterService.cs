using Synergos.CMS.Application;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves site footer and contact configuration.
///
/// v11.0.0 — reads from the active Layout Profile's FooterConfig picker when
/// available. Falls back to legacy SiteSettings tabs otherwise.
///
/// Contact info always comes from SiteSettings (site-identity, not layout).
/// </summary>
public sealed class UmbracoFooterService : IFooterService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoFooterService(IContentContextAccessor accessor) => _accessor = accessor;

    public SiteFooterConfig GetSiteFooterConfig(int rootNodeId)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);

        // Primary path — via Layout Profile → FooterConfig.
        var profile      = LayoutContentResolver.ResolveLayoutProfile(_accessor, rootNodeId);
        var footerConfig = profile is null
            ? null
            : LayoutContentResolver.ReadPickerContent(profile, "footerConfig");

        if (footerConfig is not null &&
            footerConfig.ContentType.Alias == ContentTypeKeys.Aliases.FooterConfig)
        {
            var navPick = LayoutContentResolver.ReadPickerContent(footerConfig, "footerNavigation");

            return new SiteFooterConfig(
                FooterNavigationNodeId: navPick?.Id,
                CopyrightText:          footerConfig.Value<string>("copyrightText"),
                NewsletterActionUrl:    footerConfig.Value<string>("newsletterActionUrl"),
                SocialLinks:            ss is null ? [] : SiteSettingsAccessor.BuildSocialLinks(ss));
        }

        // Legacy fallback — SiteSettings Footer tab.
        if (ss is null) return new SiteFooterConfig(null, null, null, []);

        var legacyNav = LayoutContentResolver.ReadPickerContent(ss, "footerNavigation");

        return new SiteFooterConfig(
            FooterNavigationNodeId: legacyNav?.Id,
            CopyrightText:          ss.Value<string>("footerCopy"),
            NewsletterActionUrl:    ss.Value<string>("newsletterActionUrl"),
            SocialLinks:            SiteSettingsAccessor.BuildSocialLinks(ss));
    }

    public ContactInfoConfig GetContactInfo(int rootNodeId)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null) return new ContactInfoConfig(null, null, null, null);

        return new ContactInfoConfig(
            Email:         ss.Value<string>("contactEmail"),
            Phone:         ss.Value<string>("contactPhone"),
            Address:       ss.Value<string>("contactAddress"),
            GoogleMapsUrl: ss.Value<string>("googleMapsUrl"));
    }
}
