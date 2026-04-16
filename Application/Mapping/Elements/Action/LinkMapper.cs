using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class LinkMapper(
    ContentCtaReader         cta,
    BehaviorNavigationReader nav,
    DomClassReader           cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionLink";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LinkViewModel
    {
        Cta        = cta.Read(e),
        Navigation = nav.Read(e),
        DomClass   = cls.Read(e)
    });
}
