namespace Synergos.CMS.Presentation.ViewModels.Layout;

public sealed record LayoutNavigationItemViewModel(
    string Title,
    string Url,
    bool IsActive,
    IReadOnlyList<LayoutNavigationItemViewModel> Children);
