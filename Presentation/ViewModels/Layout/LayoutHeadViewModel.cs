namespace Synergos.CMS.Presentation.ViewModels.Layout;

public sealed record LayoutHeadViewModel(
    string? ThemeColor,
    string? TrackingHeadScriptsHtml,
    IReadOnlyList<string> CdnStyleUrls,
    string? ImportMapJson);
