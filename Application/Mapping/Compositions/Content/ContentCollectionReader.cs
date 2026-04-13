using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Collection;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentCollectionReader : ICompositionReader<ContentCollectionModel>
{
    public ContentCollectionModel Read(IPublishedElement element) => new(
        CollectionTitle:       element.Value<string>(CollectionTitle),
        CollectionDescription: element.Value<string>(CollectionDescription));
}
