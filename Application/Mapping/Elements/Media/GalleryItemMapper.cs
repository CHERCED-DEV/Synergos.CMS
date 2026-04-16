using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
