using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
