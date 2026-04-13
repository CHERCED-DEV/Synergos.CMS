using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Compositions.Content;
using Synergos.CMS.Domain.Sections;
using Synergos.CMS.Domain.Shared;
using UmbracoLink = Umbraco.Cms.Core.Models.Link;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ButtonMapper(
    ContentCtaReader        cta,
    BehaviorNavigationReader nav,
    BehaviorTrackingReader  tracking,
    DomVariantReader        variant,
    DomClassReader          cls) : ISectionMapper
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

public sealed class LinkMapper(
    ContentCtaReader        cta,
    BehaviorNavigationReader nav,
    DomClassReader          cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionLink";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LinkViewModel
    {
        Cta        = cta.Read(e),
        Navigation = nav.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class ButtonGroupMapper(
    ContentCollectionReader collection,
    DomLayoutReader         layout,
    DomClassReader          cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionButtonGroup";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ButtonGroupViewModel
    {
        Collection = collection.Read(e),
        DomLayout  = layout.Read(e),
        DomClass   = cls.Read(e)
    });
}

public sealed class CtaGroupMapper(
    ContentCollectionReader collection,
    DomLayoutReader         layout,
    DomClassReader          cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionCtaGroup";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new CtaGroupViewModel
    {
        PrimaryCta   = ReadCta(e, "primary"),
        SecondaryCta = ReadCta(e, "secondary"),
        Collection   = collection.Read(e),
        DomLayout    = layout.Read(e),
        DomClass     = cls.Read(e)
    });

    private static ContentCtaModel ReadCta(IPublishedElement e, string prefix)
    {
        var raw  = e.Value<UmbracoLink>($"{prefix}CtaLink");
        var link = raw is null ? null : new Link(raw.Url ?? string.Empty, raw.Target ?? "_self", raw.Name);
        return new(
            CtaLabel:     e.Value<string>($"{prefix}CtaLabel"),
            CtaLink:      link,
            CtaTarget:    e.Value<string>($"{prefix}CtaTarget") ?? link?.Target,
            CtaAriaLabel: null);
    }
}

public sealed class ButtonContainerMapper(DomClassReader cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionButtonContainer";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new ButtonContainerViewModel
    {
        Label    = element.Value<string>("label"),
        Href     = element.Value<UmbracoLink>("href")?.Url,
        Target   = element.Value<string>("target"),
        DomClass = cls.Read(element)
    });
}
