using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Dom;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.DomAttributes;

namespace Synergos.CMS.Application.Mapping.Compositions.Dom;

public sealed class DomAttributesReader : ICompositionReader<DomAttributesModel>
{
    public DomAttributesModel Read(IPublishedElement element) => new(
        ElementId:      element.Value<string>(ElementId),
        DataAttributes: element.Value<string>(DataAttributes),
        AriaLabel:      element.Value<string>(AriaLabel),
        Role:           element.Value<string>(Role));
}
