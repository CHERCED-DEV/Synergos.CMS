using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
