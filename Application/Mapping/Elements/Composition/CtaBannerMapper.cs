using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
