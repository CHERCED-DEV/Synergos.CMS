using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
