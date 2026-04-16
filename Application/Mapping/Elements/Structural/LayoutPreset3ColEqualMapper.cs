using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
