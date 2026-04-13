namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Reads site-level SEO default values from SiteSettings.
/// Focused interface — satisfies ISP.
/// </summary>
public interface ISeoService
{
    SeoDefaultsConfig GetSeoDefaults(int rootNodeId);
}
