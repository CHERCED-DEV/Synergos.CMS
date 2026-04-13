namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Reads header-specific configuration from SiteSettings.
/// Focused interface — satisfies ISP: callers that only need header config
/// inject this instead of the full ILayoutService.
/// </summary>
public interface IHeaderService
{
    SiteHeaderConfig GetSiteHeaderConfig(int rootNodeId);
}
