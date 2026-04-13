using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Collection;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class CardElementMapper(
    ContentTextReader  textReader,
    ContentMediaReader mediaReader,
    ContentCtaReader   ctaReader,
    ContentBadgeReader badgeReader,
    DomClassReader     cls,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompCard";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new CardElementViewModel
    {
        Text       = textReader.Read(e),
        Media      = mediaReader.Read(e),
        Cta        = ctaReader.Read(e),
        Badge      = badgeReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class HeroElementMapper(
    ContentHeadingReader headingReader,
    ContentTextReader    textReader,
    ContentMediaReader   mediaReader,
    ContentCtaReader     ctaReader,
    DomVariantReader     variant,
    DomClassReader       cls,
    DomSpacingReader     spacing) : ISectionMapper
{
    public string SupportedAlias => "elementCompHero";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new HeroElementViewModel
    {
        Heading    = headingReader.Read(e),
        Text       = textReader.Read(e),
        Media      = mediaReader.Read(e),
        Cta        = ctaReader.Read(e),
        DomVariant = variant.Read(e),
        DomClass   = cls.Read(e),
        DomSpacing = spacing.Read(e)
    });
}

public sealed class FeatureGridMapper(
    ContentCollectionReader collection,
    ContentTextReader       textReader,
    ContentMediaReader      mediaReader,
    DomLayoutReader         layout,
    DomSpacingReader        spacing,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompFeatureGrid";

    public ISection? Map(IPublishedElement e)
    {
        // CTA on individual feature items within a grid is intentionally omitted
        // to stay within the 7-param limit (S107). Use FeatureItemMapper directly
        // when a standalone feature with a CTA link is needed.
        var items = e.Value<BlockListModel>(Items)
            ?.Select(b => new FeatureViewModel
            {
                Text     = textReader.Read(b.Content),
                Media    = mediaReader.Read(b.Content),
                DomClass = cls.Read(b.Content)
            })
            .ToList() ?? [];

        return new ComponentSection(new FeatureGridViewModel
        {
            Collection = collection.Read(e),
            Items      = items,
            DomLayout  = layout.Read(e),
            DomSpacing = spacing.Read(e),
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}

public sealed class CtaBannerMapper(
    ContentTextReader textReader,
    ContentCtaReader  ctaReader,
    DomVariantReader  variant,
    DomSpacingReader  spacing) : ISectionMapper
{
    public string SupportedAlias => "elementCompCtaBanner";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new CtaBannerViewModel
    {
        Text       = textReader.Read(e),
        Cta        = ctaReader.Read(e),
        DomVariant = variant.Read(e),
        DomSpacing = spacing.Read(e)
    });
}

public sealed class BannerMapper(
    ContentTextReader textReader,
    ContentCtaReader  ctaReader,
    DomVariantReader  variant,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementCompBanner";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new BannerViewModel
    {
        Text       = textReader.Read(element),
        Cta        = ctaReader.Read(element),
        DomVariant = variant.Read(element),
        DomClass   = cls.Read(element)
    });
}

public sealed class FaqListMapper(
    ContentCollectionReader   collection,
    ContentTextReader         textReader,
    BehaviorInteractionReader interaction,
    DomClassReader            cls,
    DomVariantReader          variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompFaqList";

    public ISection? Map(IPublishedElement e)
    {
        var items = e.Value<BlockListModel>(Items)
            ?.Select(b => new FaqItemViewModel
            {
                Text        = textReader.Read(b.Content),
                DomClass    = cls.Read(b.Content),
                Interaction = interaction.Read(b.Content)
            })
            .ToList() ?? [];

        return new ComponentSection(new FaqListViewModel
        {
            Collection = collection.Read(e),
            Items      = items,
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}

public sealed class TestimonialListMapper(
    ContentCollectionReader collection,
    ContentTextReader       textReader,
    ContentMediaReader      mediaReader,
    ContentBadgeReader      badgeReader,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompTestimonialList";

    public ISection? Map(IPublishedElement e)
    {
        var items = e.Value<BlockListModel>(Items)
            ?.Select(b => new TestimonialItemViewModel
            {
                Text     = textReader.Read(b.Content),
                Media    = mediaReader.Read(b.Content),
                Badge    = badgeReader.Read(b.Content),
                DomClass = cls.Read(b.Content)
            })
            .ToList() ?? [];

        return new ComponentSection(new TestimonialListViewModel
        {
            Collection = collection.Read(e),
            Items      = items,
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}

public sealed class LogoCloudMapper(
    ContentCollectionReader collection,
    ContentTextReader       textReader,
    ContentMediaReader      mediaReader,
    ContentCtaReader        ctaReader,
    DomSpacingReader        spacing,
    DomClassReader          cls) : ISectionMapper
{
    public string SupportedAlias => "elementCompLogoCloud";

    public ISection? Map(IPublishedElement e)
    {
        var items = e.Value<BlockListModel>(Items)
            ?.Select(b => new LogoItemViewModel
            {
                Media    = mediaReader.Read(b.Content),
                Text     = textReader.Read(b.Content),
                Cta      = ctaReader.Read(b.Content),
                DomClass = cls.Read(b.Content)
            })
            .ToList() ?? [];

        return new ComponentSection(new LogoCloudViewModel
        {
            Collection = collection.Read(e),
            Text       = textReader.Read(e),
            Items      = items,
            DomSpacing = spacing.Read(e),
            DomClass   = cls.Read(e)
        });
    }
}

public sealed class MediaTextSplitMapper(
    ContentTextReader  textReader,
    ContentMediaReader mediaReader,
    ContentCtaReader   ctaReader,
    DomLayoutReader    layout,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompMediaTextSplit";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new MediaTextSplitViewModel
    {
        Text       = textReader.Read(e),
        Media      = mediaReader.Read(e),
        Cta        = ctaReader.Read(e),
        DomLayout  = layout.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class InfoBlockElementMapper(
    ContentTextReader textReader,
    ContentCtaReader  ctaReader,
    DomVariantReader  variant,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementCompInfoBlock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new InfoBlockViewModel
    {
        Text       = textReader.Read(e),
        Cta        = ctaReader.Read(e),
        DomVariant = variant.Read(e),
        DomClass   = cls.Read(e)
    });
}

internal sealed class AccordionMapper(
    ContentCollectionReader   collection,
    ContentTextReader         text,
    DomClassReader            cls,
    BehaviorInteractionReader interaction) : ISectionMapper
{
    public string SupportedAlias => "elementCompAccordion";
    public ISection? Map(IPublishedElement e)
    {
        var items = e.Value<BlockListModel>(Items)
            ?.Select(b => new AccordionItemViewModel
            {
                Text     = text.Read(b.Content),
                DomClass = cls.Read(b.Content)
            }).ToList() ?? [];

        return new ComponentSection(new AccordionViewModel
        {
            Collection  = collection.Read(e),
            Text        = text.Read(e),
            Items       = items,
            DomClass    = cls.Read(e),
            Interaction = interaction.Read(e)
        });
    }
}
