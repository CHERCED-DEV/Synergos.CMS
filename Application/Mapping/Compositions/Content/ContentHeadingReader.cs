using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Heading;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentHeadingReader : ICompositionReader<ContentHeadingModel>
{
    public ContentHeadingModel Read(IPublishedElement element) => new(
        HeadingText:       element.Value<string>(HeadingText),
        HeadingLevel:      element.Value<string>(HeadingLevel),
        HeadingTagOverride: element.Value<string>(HeadingTagOverride));
}
