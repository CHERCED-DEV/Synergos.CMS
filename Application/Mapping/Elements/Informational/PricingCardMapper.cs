using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class PricingCardMapper(
    ContentTextReader    text,
    ContentCtaReader     cta,
    ContentBadgeReader   badge,
    ContentPricingReader pricing,
    DomClassReader       cls,
    DomVariantReader     variant) : ISectionMapper
{
    public string SupportedAlias => "elementInfoPricingCard";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new PricingCardViewModel
    {
        Text       = text.Read(e),
        Cta        = cta.Read(e),
        Badge      = badge.Read(e),
        Pricing    = pricing.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
