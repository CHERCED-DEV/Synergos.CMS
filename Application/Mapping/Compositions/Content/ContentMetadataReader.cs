using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Metadata;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentMetadataReader : ICompositionReader<ContentMetadataModel>
{
    public ContentMetadataModel Read(IPublishedElement element) => new(
        Tags:     element.Value<IEnumerable<string>>(Tags)?.ToList() ?? [],
        Category: element.Value<string>(Category),
        Keywords: element.Value<string>(Keywords));
}
