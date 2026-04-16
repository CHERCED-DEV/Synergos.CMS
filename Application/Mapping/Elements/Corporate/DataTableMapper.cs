using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class DataTableMapper(
    ContentTextReader       text,
    ContentCollectionReader collection,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpDataTable";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new DataTableViewModel
    {
        Text       = text.Read(e),
        Collection = collection.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
