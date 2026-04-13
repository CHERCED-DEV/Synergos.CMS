using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Embed;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentEmbedReader : ICompositionReader<ContentEmbedModel>
{
    public ContentEmbedModel Read(IPublishedElement element) => new(
        EmbedUrl:   element.Value<string>(EmbedUrl),
        EmbedType:  element.Value<string>(EmbedType),
        EmbedTitle: element.Value<string>(EmbedTitle));
}
