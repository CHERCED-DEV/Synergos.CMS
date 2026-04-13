using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Seo;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Application.Mapping.Compositions.Seo;

public sealed class SeoReader : ICompositionReader<SeoModel>
{
    public SeoModel Read(IPublishedElement element)
    {
        var ogMediaContent = element.Value<IPublishedContent>("ogImage");
        var ogImage = ogMediaContent is null
            ? null
            : new Image(ogMediaContent.Url(), ogMediaContent.Value<string>("altText") ?? ogMediaContent.Name);

        return new(
            SeoTitle:       element.Value<string>("seoTitle"),
            SeoDescription: element.Value<string>("seoDescription"),
            OgTitle:        element.Value<string>("ogTitle"),
            OgDescription:  element.Value<string>("ogDescription"),
            OgImage:        ogImage,
            Canonical:      element.Value<string>("canonical"),
            Robots:         element.Value<string>("robots"));
    }
}
