using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class TimelineItemMapper(
    ContentTextReader textReader,
    ContentDateReader dateReader,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementInfoTimelineItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new TimelineItemViewModel
    {
        Text       = textReader.Read(e),
        Date       = dateReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
