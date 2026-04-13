using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

// ── Structural Mappers ────────────────────────────────────────────────────
// These elements carry no content — only DOM configuration.
// All readers are optional; missing data simply yields null model properties.

public sealed class SectionStructMapper(
    DomLayoutReader    layout,
    DomSpacingReader   spacing,
    DomClassReader     cls,
    DomAttributesReader attrs) : ISectionMapper
{
    public string SupportedAlias => "elementStructSection";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new SectionViewModel
    {
        DomLayout     = layout.Read(e),
        DomSpacing    = spacing.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class ContainerStructMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementStructContainer";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ContainerViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class GridStructMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls) : ISectionMapper
{
    public string SupportedAlias => "elementStructGrid";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new GridViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class ColumnStructMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls) : ISectionMapper
{
    public string SupportedAlias => "elementStructColumn";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ColumnViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class StackStructMapper(
    DomSpacingReader spacing,
    DomClassReader   cls) : ISectionMapper
{
    public string SupportedAlias => "elementStructStack";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new StackViewModel
    {
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class DividerStructMapper(
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementStructDivider";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new DividerViewModel
    {
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class SpacerStructMapper(
    DomSpacingReader spacing,
    DomClassReader   cls) : ISectionMapper
{
    public string SupportedAlias => "elementStructSpacer";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new SpacerViewModel
    {
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class LayoutPreset1ColMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "layoutPreset1Col";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LayoutPreset1ColViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class LayoutPreset2ColEqualMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "layoutPreset2ColEqual";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LayoutPreset2ColEqualViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class LayoutPreset3ColEqualMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "layoutPreset3ColEqual";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LayoutPreset3ColEqualViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class LayoutPreset4ColEqualMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "layoutPreset4ColEqual";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LayoutPreset4ColEqualViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class LayoutPresetMainSidebarMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "layoutPresetMainSidebar";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LayoutPresetMainSidebarViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
