using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ButtonGroupMapper(
    ContentCollectionReader collection,
    DomLayoutReader         layout,
    DomClassReader          cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionButtonGroup";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ButtonGroupViewModel
    {
        Collection = collection.Read(e),
        DomLayout  = layout.Read(e),
        DomClass   = cls.Read(e)
    });
}
