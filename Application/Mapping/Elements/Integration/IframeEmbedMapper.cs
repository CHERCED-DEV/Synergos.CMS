using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class IframeEmbedMapper(
    ContentEmbedReader embedReader,
    DomSpacingReader   spacing,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementIntIframeEmbed";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new IframeEmbedViewModel
    {
        Embed      = embedReader.Read(e),
        DomSpacing = spacing.Read(e),
        DomVariant = variant.Read(e)
    });
}
