using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Dom;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.DomClass;

namespace Synergos.CMS.Application.Mapping.Compositions.Dom;

public sealed class DomClassReader : ICompositionReader<DomClassModel>
{
    public DomClassModel Read(IPublishedElement element) => new(
        ClassList: element.Value<string>(ClassList));
}
