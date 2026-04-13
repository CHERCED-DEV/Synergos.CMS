using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Dom;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.DomVariant;

namespace Synergos.CMS.Application.Mapping.Compositions.Dom;

public sealed class DomVariantReader : ICompositionReader<DomVariantModel>
{
    public DomVariantModel Read(IPublishedElement element) => new(
        Variant: element.Value<string>(Variant),
        Theme:   element.Value<string>(Theme));
}
