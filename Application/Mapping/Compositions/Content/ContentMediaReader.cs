using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using Synergos.CMS.Domain.Shared;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Media;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentMediaReader : ICompositionReader<ContentMediaModel>
{
    public ContentMediaModel Read(IPublishedElement element)
    {
        var media = element.Value<IPublishedContent>(Picker);
        var image = media is null ? null : new Image(media.Url(), media.Value<string>(AltText) ?? media.Name);

        return new(
            Media:            image,
            AltText:          element.Value<string>(AltText),
            MediaTitle:       element.Value<string>(MediaTitle),
            MediaDescription: element.Value<string>(MediaDescription));
    }
}
