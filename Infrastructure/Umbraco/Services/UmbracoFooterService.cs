using Synergos.CMS.Application;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves site footer and contact configuration from SiteSettings.
///
/// Responsibilities:
///   - GetSiteFooterConfig — footer nav node ID, copyright text, newsletter URL, social links
///   - GetContactInfo      — email, phone, address, Google Maps URL
/// </summary>
public sealed class UmbracoFooterService : IFooterService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoFooterService(IContentContextAccessor accessor) => _accessor = accessor;

    public SiteFooterConfig GetSiteFooterConfig(int rootNodeId)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null) return new SiteFooterConfig(null, null, null, []);

        var navPick = LayoutContentResolver.ReadPickerContent(ss, "footerNavigation");

        return new SiteFooterConfig(
            FooterNavigationNodeId: navPick?.Id,
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
