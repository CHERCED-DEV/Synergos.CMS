using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ImageMapper(
    ContentMediaReader mediaReader,
    DomSpacingReader   spacing,
    DomClassReader     cls) : ISectionMapper
{
    public string SupportedAlias => "elementMediaImage";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ImageElementViewModel
    {
        Media      = mediaReader.Read(e),
        DomSpacing = spacing.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class VideoElementMapper(
    ContentEmbedReader embedReader,
    DomSpacingReader   spacing,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementMediaVideo";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new VideoElementViewModel
    {
        Embed      = embedReader.Read(e),
        DomSpacing = spacing.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class IconMapper(
    ContentMediaReader mediaReader,
    ContentBadgeReader badgeReader,
    DomClassReader     cls,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementMediaIcon";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new IconViewModel
    {
        Media      = mediaReader.Read(e),
        Badge      = badgeReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class GalleryItemMapper(
    ContentMediaReader mediaReader,
    ContentTextReader  textReader,
    DomClassReader     cls) : ISectionMapper
{
    public string SupportedAlias => "elementMediaGalleryItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new GalleryItemViewModel
    {
        Media    = mediaReader.Read(e),
        Text     = textReader.Read(e),
        DomClass = cls.Read(e)
    });
}

public sealed class LogoItemMapper(
    ContentMediaReader mediaReader,
    ContentTextReader  textReader,
    ContentCtaReader   ctaReader,
    DomClassReader     cls) : ISectionMapper
{
    public string SupportedAlias => "elementMediaLogoItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LogoItemViewModel
    {
        Media    = mediaReader.Read(e),
        Text     = textReader.Read(e),
        Cta      = ctaReader.Read(e),
        DomClass = cls.Read(e)
    });
}

internal sealed class AvatarMapper(
    ContentMediaReader media,
    ContentTextReader text,
    DomClassReader cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementMediaAvatar";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new AvatarViewModel
    {
        Media      = media.Read(e),
        Text       = text.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
