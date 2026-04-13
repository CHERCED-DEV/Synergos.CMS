using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Dom;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.DomLayout;

namespace Synergos.CMS.Application.Mapping.Compositions.Dom;

public sealed class DomLayoutReader : ICompositionReader<DomLayoutModel>
{
    public DomLayoutModel Read(IPublishedElement element) => new(
        ContainerType: element.Value<string>(ContainerType),
        Alignment:     element.Value<string>(Alignment),
        Direction:     element.Value<string>(Direction));
}
