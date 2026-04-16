using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class TabGroupMapper(
    ContentCollectionReader   collection,
    ContentTextReader         text,
    DomClassReader            cls,
    BehaviorInteractionReader interaction) : ISectionMapper
{
    public string SupportedAlias => "elementCorpTabGroup";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new TabGroupViewModel
    {
        Collection  = collection.Read(e),
        Text        = text.Read(e),
        DomClass    = cls.Read(e),
        Interaction = interaction.Read(e)
    });
}
