using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Dom;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.DomSpacing;

namespace Synergos.CMS.Application.Mapping.Compositions.Dom;

public sealed class DomSpacingReader : ICompositionReader<DomSpacingModel>
{
    public DomSpacingModel Read(IPublishedElement element) => new(
        Margin:  element.Value<string>(Margin),
        Padding: element.Value<string>(Padding),
        Gap:     element.Value<string>(Gap));
}
