namespace Synergos.CMS.Application.Output;

/// <summary>
/// Tree node for Block Grid rendering with nested areas.
///
/// Wraps a SectionView with its column/row span and child nodes.
/// Structural blocks (Section, Grid, Column) carry children;
/// content blocks (leaf nodes) have an empty Children list.
/// </summary>
public sealed record SectionNode(
    SectionView View,
    int         ColumnSpan,
    int         RowSpan,
    IReadOnlyList<SectionNode> Children);
