using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Async;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

public sealed class BehaviorAsyncReader : ICompositionReader<BehaviorAsyncModel>
{
    public BehaviorAsyncModel Read(IPublishedElement element) => new(
        ApiEndpoint: element.Value<string>(ApiEndpoint),
        Method:      element.Value<string>(Method));
}
