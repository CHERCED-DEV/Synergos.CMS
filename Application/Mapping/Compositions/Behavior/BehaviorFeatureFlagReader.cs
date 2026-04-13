using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.FeatureFlag;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

public sealed class BehaviorFeatureFlagReader : ICompositionReader<BehaviorFeatureFlagModel>
{
    public BehaviorFeatureFlagModel Read(IPublishedElement element) => new(
        FeatureKey: element.Value<string>(FeatureKey),
        IsEnabled:  element.Value<bool>(IsEnabled));
}
