using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class TestimonialCarouselMapper(
    TestimonialItemMapper itemMapper,
    ContentTextReader     textReader,
    DomClassReader        cls,
    DomSpacingReader      spacing,
    DomVariantReader      variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompTestimonialCarousel";

    public ISection? Map(IPublishedElement e)
    {
        var items = e.Value<BlockListModel>("testimonialItems")
            ?.Where(b => b.Content.ContentType.Alias == "elementInfoTestimonialItem")
            .Select(b => itemMapper.ReadAsItem(b.Content))
            .ToList() ?? [];

        var columns = e.Value<int>("gridColumns");

        return new ComponentSection(new TestimonialCarouselViewModel
        {
            Text       = textReader.Read(e),
            Items      = items,
            Columns    = columns > 0 ? columns : null,
            Gap        = e.Value<string>("gridGap"),
            DomClass   = cls.Read(e),
            DomSpacing = spacing.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}
