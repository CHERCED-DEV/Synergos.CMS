using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
