using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using Synergos.CMS.Domain.Shared;
using UmbracoLink = Umbraco.Cms.Core.Models.Link;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Cta;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

public sealed class ContentCtaReader : ICompositionReader<ContentCtaModel>
{
    public ContentCtaModel Read(IPublishedElement element)
    {
        var raw    = element.Value<UmbracoLink>(CtaLink);
        var link   = raw is null ? null : new Link(raw.Url ?? string.Empty, raw.Target ?? "_self", raw.Name);
        var target = element.Value<string>(CtaTarget);

        return new(
            CtaLabel:    element.Value<string>(CtaLabel),
            CtaLink:     link,
            CtaTarget:   target ?? link?.Target,
            CtaAriaLabel: element.Value<string>(CtaAriaLabel));
    }
}
