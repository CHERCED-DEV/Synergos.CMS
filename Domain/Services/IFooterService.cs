namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Reads footer and contact configuration from SiteSettings.
/// Focused interface — satisfies ISP.
/// </summary>
public interface IFooterService
{
    SiteFooterConfig  GetSiteFooterConfig(int rootNodeId);
    ContactInfoConfig GetContactInfo(int rootNodeId);
}
