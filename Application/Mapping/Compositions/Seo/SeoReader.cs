using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Seo;
using Synergos.CMS.Domain.Shared;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Seo;

namespace Synergos.CMS.Application.Mapping.Compositions.Seo;

public sealed class SeoReader : ICompositionReader<SeoModel>
{
    public SeoModel Read(IPublishedElement element)
    {
        var ogMediaContent = element.Value<IPublishedContent>(OgImage);
        var ogImage = ogMediaContent is null
            ? null
            : new Image(ogMediaContent.Url(), ogMediaContent.Value<string>(AltText) ?? ogMediaContent.Name);

        return new(
            SeoTitle:       element.Value<string>(SeoTitle),
            SeoDescription: element.Value<string>(SeoDescription),
            OgTitle:        element.Value<string>(OgTitle),
            OgDescription:  element.Value<string>(OgDescription),
            OgImage:        ogImage,
            Canonical:      element.Value<string>(Canonical),
            Robots:         element.Value<string>(Robots));
    }
}
