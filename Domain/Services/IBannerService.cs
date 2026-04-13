namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Reads global promotional banner configuration from SiteSettings.
/// Focused interface — satisfies ISP.
/// </summary>
public interface IBannerService
{
    BannerConfig? GetBannerConfig(int rootNodeId);
}
