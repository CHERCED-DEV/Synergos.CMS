using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class AttributedQuoteMapper(
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextAttributedQuote";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new AttributedQuoteViewModel
    {
        QuoteText   = element.Value<string>("quoteText"),
        QuoteAuthor = element.Value<string>("quoteAuthor"),
        QuoteRole   = element.Value<string>("quoteRole"),
        DomClass    = cls.Read(element),
        DomVariant  = variant.Read(element)
    });
}
