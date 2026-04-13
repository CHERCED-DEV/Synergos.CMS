using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class FeatureJourneyMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceFeatureJourney";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new FeatureJourneyViewModel
    {
        Text       = text.Read(e),
        Collection = collection.Read(e),
        DomVariant = variant.Read(e),
        DomClass   = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class InsightExplorerMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceInsightExplorer";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new InsightExplorerViewModel
    {
        Text       = text.Read(e),
        Collection = collection.Read(e),
        DomVariant = variant.Read(e),
        DomClass   = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class MediaExplorerMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    ContentMetadataReader   metadata,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceMediaExplorer";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new MediaExplorerViewModel
    {
        Text       = text.Read(e),
        Collection = collection.Read(e),
        Metadata   = metadata.Read(e),
        DomVariant = variant.Read(e),
        DomClass   = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class ContentCarouselMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    ContentMetadataReader   metadata,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceContentCarousel";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ContentCarouselViewModel
    {
        Text          = text.Read(e),
        Collection    = collection.Read(e),
        Metadata      = metadata.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class QuizFlowMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceQuizFlow";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new QuizFlowViewModel
    {
        Text          = text.Read(e),
        Collection    = collection.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class FilterBoardMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    ContentMetadataReader   metadata,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceFilterBoard";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new FilterBoardViewModel
    {
        Text          = text.Read(e),
        Collection    = collection.Read(e),
        Metadata      = metadata.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class RatingWidgetMapper(
    ContentTextReader     text,
    ContentMetadataReader metadata,
    DomVariantReader      variant,
    DomClassReader        cls,
    DomAttributesReader   attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceRatingWidget";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new RatingWidgetViewModel
    {
        Text          = text.Read(e),
        Metadata      = metadata.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class CountdownClockMapper(
    ContentTextReader     text,
    ContentMetadataReader metadata,
    DomVariantReader      variant,
    DomClassReader        cls,
    DomAttributesReader   attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceCountdownClock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new CountdownClockViewModel
    {
        Text          = text.Read(e),
        Metadata      = metadata.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class NotificationStackMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    DomVariantReader        variant,
    DomClassReader          cls,
    DomAttributesReader     attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceNotificationStack";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new NotificationStackViewModel
    {
        Text          = text.Read(e),
        Collection    = collection.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}
