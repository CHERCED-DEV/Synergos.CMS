namespace Synergos.CMS.Presentation.ViewModels.Layout;

public sealed record CultureOptionViewModel(
    string Label,
    string IsoCode,
    string SwitchUrl,
    bool IsCurrent);

public sealed record LayoutHeaderViewModel(
    LayoutBrandViewModel Brand,
    LayoutThemeViewModel Theme,
    IReadOnlyList<LayoutNavigationItemViewModel> NavigationItems,
    IReadOnlyList<CultureOptionViewModel> CultureOptions,
    string? ApplicationRootName,
    string? NavigationRootName,
    string? CtaLabel = null,
    string? CtaUrl = null);
