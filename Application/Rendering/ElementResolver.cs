using Umbraco.Cms.Core.Models.Blocks;
using Synergos.CMS.Application.Mapping;
using Synergos.CMS.Application.Output;

namespace Synergos.CMS.Application.Rendering;

/// <summary>
/// Resolves a Block Grid or Block List item to a SectionView.
///
/// Delegates to SectionMapperDispatcher (which holds all ISectionMapper registrations).
/// Supports both BlockGridItem and BlockListItem inputs so the same resolver can be
/// used regardless of the underlying editor type.
///
/// v4.0.0 — Added support for nested areas in Block Grid items.
/// Structural blocks (Section, Grid, Column) can contain child blocks
/// via AreaConfigurations. ResolveWithChildren walks the tree recursively.
/// </summary>
public sealed class ElementResolver
{
    private readonly SectionMapperDispatcher _dispatcher;

    public ElementResolver(SectionMapperDispatcher dispatcher)
        => _dispatcher = dispatcher;

    /// <summary>Resolves a Block List item.</summary>
    public SectionView? Resolve(BlockListItem block)
        => _dispatcher.Dispatch(block);

    /// <summary>Resolves a Block Grid item (flat — ignores areas).</summary>
    public SectionView? Resolve(BlockGridItem block)
        => _dispatcher.Dispatch(block);

    /// <summary>
    /// Resolves a Block Grid item with its nested area children.
    /// Returns a SectionNode that preserves the tree structure for rendering.
    /// </summary>
    public SectionNode? ResolveTree(BlockGridItem block)
    {
        var sectionView = Resolve(block);
        if (sectionView is null) return null;

        var children = new List<SectionNode>();

        foreach (var area in block.Areas)
        {
            foreach (var child in area)
            {
                var childNode = ResolveTree(child);
                if (childNode is not null)
                    children.Add(childNode);
            }
        }

        return new SectionNode(
            sectionView,
            block.ColumnSpan,
            block.RowSpan,
            children);
    }
}
