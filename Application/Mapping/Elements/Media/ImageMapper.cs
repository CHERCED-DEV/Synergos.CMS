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
