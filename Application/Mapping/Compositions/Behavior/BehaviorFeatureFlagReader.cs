using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.FeatureFlag;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

/// <summary>
/// Reads the <c>compBehaviorFeatureFlag</c> composition properties —
/// feature key + enabled toggle — into a <see cref="BehaviorFeatureFlagModel"/>.
/// Elements can carry their own flag so editors can gate rendering without
/// changing app configuration.
/// </summary>
public sealed class BehaviorFeatureFlagReader : ICompositionReader<BehaviorFeatureFlagModel>
{
    public BehaviorFeatureFlagModel Read(IPublishedElement element) => new(
        FeatureKey: element.Value<string>(FeatureKey),
        IsEnabled:  element.Value<bool>(IsEnabled));
}
