using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Date;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentDateReader : ICompositionReader<ContentDateModel>
{
    public ContentDateModel Read(IPublishedElement element) => new(
        PublishDate: element.Value<DateTime?>(PublishDate),
        UpdateDate:  element.Value<DateTime?>(UpdateDate));
}
