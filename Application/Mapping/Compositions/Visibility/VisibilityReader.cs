using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Visibility;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Visibility;

namespace Synergos.CMS.Application.Mapping.Compositions.Visibility;

public sealed class VisibilityReader : ICompositionReader<VisibilityModel>
{
    public VisibilityModel Read(IPublishedElement element) => new(
        IsHidden:            element.Value<bool>(IsHidden),
        VisibilityStart:     element.Value<DateTime?>(VisibilityStart),
        VisibilityEnd:       element.Value<DateTime?>(VisibilityEnd),
        VisibilityCondition: element.Value<string>(VisibilityCondition));
}
