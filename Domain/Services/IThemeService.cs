namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Reads brand, theme, and per-page layout configuration.
/// Covers: site-level LayoutConfig (brand + theme variant), ThemeConfig (CSS tokens),
/// and per-page PageLayoutConfig (CompDomLayoutProfile composition).
/// Focused interface — satisfies ISP.
/// </summary>
public interface IThemeService
{
    LayoutConfig     GetLayoutConfig(int rootNodeId);
    ThemeConfig?     GetThemeConfig(int rootNodeId);
    PageLayoutConfig GetPageLayoutConfig(int? pageId);
}
