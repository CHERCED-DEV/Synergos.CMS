namespace Synergos.CMS.Presentation.ViewModels.Layout;

public sealed record LayoutBrandViewModel(
    string HomeUrl,
    string? SiteName,
    string? SiteTagline,
    string? LogoUrl,
    string? LogoAlt);
