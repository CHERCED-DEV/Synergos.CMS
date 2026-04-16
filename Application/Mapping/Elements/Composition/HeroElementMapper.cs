using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
