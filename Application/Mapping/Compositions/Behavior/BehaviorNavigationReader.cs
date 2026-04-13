using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Navigation;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

public sealed class BehaviorNavigationReader : ICompositionReader<BehaviorNavigationModel>
{
    public BehaviorNavigationModel Read(IPublishedElement element) => new(
        NavigateTo:    element.Value<string>(NavigateTo),
        NavigationType: element.Value<string>(NavigationType));
}
