using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class BadgeMapper(
    ContentBadgeReader badgeReader,
    DomClassReader     cls,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementInfoBadge";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new BadgeViewModel
    {
        Badge      = badgeReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class StatMapper(
    ContentTextReader textReader,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementInfoStat";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new StatViewModel
    {
        Text       = textReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class FeatureItemMapper(
    ContentTextReader  textReader,
    ContentMediaReader mediaReader,
    ContentCtaReader   ctaReader,
    DomClassReader     cls,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementInfoFeature";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new FeatureViewModel
    {
        Text       = textReader.Read(e),
        Media      = mediaReader.Read(e),
        Cta        = ctaReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class KeyValueMapper(
    ContentTextReader textReader,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementInfoKeyValue";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new KeyValueViewModel
    {
        Text     = textReader.Read(e),
        DomClass = cls.Read(e)
    });
}

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

public sealed class FaqItemMapper(
    ContentTextReader        textReader,
    DomClassReader           cls,
    BehaviorInteractionReader interaction) : ISectionMapper
{
    public string SupportedAlias => "elementInfoFaqItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new FaqItemViewModel
    {
        Text        = textReader.Read(e),
        DomClass    = cls.Read(e),
        Interaction = interaction.Read(e)
    });
}

public sealed class TestimonialItemMapper(
    ContentTextReader  textReader,
    ContentMediaReader mediaReader,
    ContentBadgeReader badgeReader,
    DomClassReader     cls) : ISectionMapper
{
    public string SupportedAlias => "elementInfoTestimonialItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new TestimonialItemViewModel
    {
        Text     = textReader.Read(e),
        Media    = mediaReader.Read(e),
        Badge    = badgeReader.Read(e),
        DomClass = cls.Read(e)
    });
}

internal sealed class PricingCardMapper(
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
