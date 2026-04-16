using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class TestimonialItemMapper(
    ContentTextReader  textReader,
    ContentMediaReader mediaReader,
    ContentBadgeReader badgeReader,
    DomClassReader     cls) : ISectionMapper
{
    public string SupportedAlias => "elementInfoTestimonialItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(ReadAsItem(e));

    public TestimonialItemViewModel ReadAsItem(IPublishedElement e) => new()
    {
        Text     = textReader.Read(e),
        Media    = mediaReader.Read(e),
        Badge    = badgeReader.Read(e),
        DomClass = cls.Read(e)
    };
}
