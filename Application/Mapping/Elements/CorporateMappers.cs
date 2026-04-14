using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class TabGroupMapper(
    ContentCollectionReader collection,
    ContentTextReader       text,
    DomClassReader          cls,
    BehaviorInteractionReader interaction) : ISectionMapper
{
    public string SupportedAlias => "elementCorpTabGroup";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new TabGroupViewModel
    {
        Collection  = collection.Read(e),
        Text        = text.Read(e),
        DomClass    = cls.Read(e),
        Interaction = interaction.Read(e)
    });
}

public sealed class AlertBarMapper(
    ContentTextReader text,
    ContentCtaReader  cta,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpAlertBar";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new AlertBarViewModel
    {
        Text        = text.Read(e),
        Cta         = cta.Read(e),
        DomClass    = cls.Read(e),
        DomVariant  = variant.Read(e),
        Dismissible = e.Value<bool?>("dismissible"),
    });
}

public sealed class AlertBoxMapper(
    ContentTextReader text,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementCorpAlertBox";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new AlertBoxViewModel
    {
        Text        = text.Read(element),
        AlertType   = element.Value<string>("alertType"),
        Dismissible = element.Value<bool?>("dismissible"),
        DomClass    = cls.Read(element)
    });
}

public sealed class BannerSliderMapper(
    ContentCollectionReader collection,
    ContentTextReader       text,
    ContentMediaReader      media,
    ContentCtaReader        cta,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpBannerSlider";

    public ISection? Map(IPublishedElement e)
    {
        var slides = e.Value<BlockListModel>("slides")
            ?.Select(b => new BannerSlideViewModel
            {
                Text  = text.Read(b.Content),
                Media = media.Read(b.Content),
                Cta   = cta.Read(b.Content),
            })
            .ToList() ?? [];

        return new ComponentSection(new BannerSliderViewModel
        {
            Collection = collection.Read(e),
            Text       = text.Read(e),
            Slides     = slides,
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}

public sealed class NewsletterFormMapper(
    ContentTextReader   text,
    ContentCtaReader    cta,
    BehaviorAsyncReader asyncReader,
    DomClassReader      cls) : ISectionMapper
{
    public string SupportedAlias => "elementCorpNewsletterForm";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new NewsletterFormViewModel
    {
        Text     = text.Read(e),
        Cta      = cta.Read(e),
        Async    = asyncReader.Read(e),
        DomClass = cls.Read(e)
    });
}

public sealed class SocialShareMapper(
    ContentCollectionReader collection,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpSocialShare";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new SocialShareViewModel
    {
        Collection = collection.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class DataTableMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpDataTable";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new DataTableViewModel
    {
        Text       = text.Read(e),
        Collection = collection.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class ContactInfoMapper(
    ContentTextReader     text,
    ContentCtaReader      cta,
    ContentLocationReader location,
    DomClassReader        cls,
    DomVariantReader      variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpContactInfo";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ContactInfoViewModel
    {
        Text       = text.Read(e),
        Cta        = cta.Read(e),
        Location   = location.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class MapEmbedMapper(
    ContentEmbedReader    embed,
    ContentLocationReader location,
    DomSpacingReader      spacing,
    DomClassReader        cls) : ISectionMapper
{
    public string SupportedAlias => "elementCorpMapEmbed";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new MapEmbedViewModel
    {
        Embed      = embed.Read(e),
        Location   = location.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class MissionBlockMapper(
    ContentTextReader  text,
    ContentMediaReader media,
    DomClassReader     cls,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpMissionBlock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new MissionBlockViewModel
    {
        Text       = text.Read(e),
        Media      = media.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
