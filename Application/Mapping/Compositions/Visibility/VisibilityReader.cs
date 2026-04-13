using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Visibility;

namespace Synergos.CMS.Application.Mapping.Compositions.Visibility;

public sealed class VisibilityReader : ICompositionReader<VisibilityModel>
{
    public VisibilityModel Read(IPublishedElement element) => new(
        IsHidden:            element.Value<bool>("isHidden"),
        VisibilityStart:     element.Value<DateTime?>("visibilityStart"),
        VisibilityEnd:       element.Value<DateTime?>("visibilityEnd"),
        VisibilityCondition: element.Value<string>("visibilityCondition"));
}
