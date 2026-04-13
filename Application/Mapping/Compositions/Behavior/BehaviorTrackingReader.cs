using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Tracking;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

public sealed class BehaviorTrackingReader : ICompositionReader<BehaviorTrackingModel>
{
    public BehaviorTrackingModel Read(IPublishedElement element) => new(
        EventName:     element.Value<string>(EventName),
        EventCategory: element.Value<string>(EventCategory),
        EventLabel:    element.Value<string>(EventLabel));
}
