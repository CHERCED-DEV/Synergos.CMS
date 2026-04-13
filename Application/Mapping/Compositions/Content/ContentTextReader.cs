using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using Synergos.CMS.Application.Mapping;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Text;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentTextReader : ICompositionReader<ContentTextModel>
{
    public ContentTextModel Read(IPublishedElement element) => new(
        Title:    element.Value<string>(Title),
        Subtitle: element.Value<string>(Subtitle),
        Body:     element.ReadHtml(Body),
        Summary:  element.Value<string>(Summary),
        Caption:  element.Value<string>(Caption));
}
