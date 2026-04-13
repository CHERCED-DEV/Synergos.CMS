using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Badge;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentBadgeReader : ICompositionReader<ContentBadgeModel>
{
    public ContentBadgeModel Read(IPublishedElement element) => new(
        BadgeText: element.Value<string>(BadgeText),
        BadgeType: element.Value<string>(BadgeType));
}
