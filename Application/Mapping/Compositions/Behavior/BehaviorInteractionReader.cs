using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Interaction;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

public sealed class BehaviorInteractionReader : ICompositionReader<BehaviorInteractionModel>
{
    public BehaviorInteractionModel Read(IPublishedElement element) => new(
        InteractionType:   element.Value<string>(InteractionType),
        InteractionAction: element.Value<string>(InteractionAction));
}
