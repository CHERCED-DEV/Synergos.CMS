using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class FaqItemMapper(
    ContentTextReader        textReader,
    DomClassReader           cls,
    BehaviorInteractionReader interaction) : ISectionMapper
{
    public string SupportedAlias => "elementInfoFaqItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(ReadAsItem(e));

    public FaqItemViewModel ReadAsItem(IPublishedElement e) => new()
    {
        Text        = textReader.Read(e),
        DomClass    = cls.Read(e),
        Interaction = interaction.Read(e)
    };
}
