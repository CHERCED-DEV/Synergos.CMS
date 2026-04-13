namespace Synergos.CMS.Application.MultiApp;

public sealed record LayoutNavigationNode(
    string Title,
    string Url,
    bool IsActive,
    IReadOnlyList<LayoutNavigationNode> Children);
