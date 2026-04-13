using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using Synergos.CMS.Domain.Shared;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Author;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentAuthorReader : ICompositionReader<ContentAuthorModel>
{
    public ContentAuthorModel Read(IPublishedElement element)
    {
        var media = element.Value<IPublishedContent>(AuthorImage);
        var image = media is null ? null : new Image(media.Url(), media.Name);

        return new(
            AuthorName:  element.Value<string>(AuthorName),
            AuthorRole:  element.Value<string>(AuthorRole),
            AuthorImage: image);
    }
}
