namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Reads alert bar configuration from SiteSettings.
/// Focused interface — satisfies ISP.
/// </summary>
public interface IAlertBarService
{
    AlertBarConfig? GetAlertBarConfig(int rootNodeId);
}
