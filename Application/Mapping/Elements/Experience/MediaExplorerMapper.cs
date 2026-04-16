using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
        Text          = text.Read(e),
        Collection    = collection.Read(e),
        Metadata      = metadata.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}
