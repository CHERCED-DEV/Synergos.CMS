using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ButtonMapper(
    ContentCtaReader         cta,
    BehaviorNavigationReader nav,
    BehaviorTrackingReader   tracking,
    DomVariantReader         variant,
    DomClassReader           cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionButton";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ButtonViewModel
    {
        Cta        = cta.Read(e),
        Navigation = nav.Read(e),
        Tracking   = tracking.Read(e),
        DomVariant = variant.Read(e),
        DomClass   = cls.Read(e)
    });
}
