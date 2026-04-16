using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class AccordionGroupMapper(
    FaqItemMapper     itemMapper,
    ContentTextReader textReader,
    DomClassReader    cls,
    DomSpacingReader  spacing,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompAccordionGroup";

    public ISection? Map(IPublishedElement e)
    {
        var items = e.Value<BlockListModel>("faqItems")
            ?.Where(b => b.Content.ContentType.Alias == "elementInfoFaqItem")
            .Select(b => itemMapper.ReadAsItem(b.Content))
            .ToList() ?? [];

        return new ComponentSection(new AccordionGroupViewModel
        {
            Text       = textReader.Read(e),
            Items      = items,
            DomClass   = cls.Read(e),
            DomSpacing = spacing.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}
