using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Dom;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.DomVisibility;

namespace Synergos.CMS.Application.Mapping.Compositions.Dom;

public sealed class DomVisibilityReader : ICompositionReader<DomVisibilityModel>
{
    public DomVisibilityModel Read(IPublishedElement element) => new(
        HideOnMobile:  element.Value<bool>(HideOnMobile),
        HideOnDesktop: element.Value<bool>(HideOnDesktop));
}
