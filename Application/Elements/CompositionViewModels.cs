using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class CardElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Card";
    public override string BlockClass => "sg-element--comp-card";
    public override string Alias      => "elementCompCard";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
    public ContentBadgeModel? Badge { get; init; }
}

public sealed class HeroElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Hero";
    public override string BlockClass => "sg-element--comp-hero";
    public override string Alias      => "elementCompHero";

    public ContentHeadingModel? Heading { get; init; }
    public ContentTextModel?    Text    { get; init; }
    public ContentMediaModel?   Media   { get; init; }
    public ContentCtaModel?     Cta     { get; init; }
}

public sealed class FeatureGridViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FeatureGrid";
    public override string BlockClass => "sg-element--comp-feature-grid";
    public override string Alias      => "elementCompFeatureGrid";

    public ContentCollectionModel?         Collection { get; init; }
    public IReadOnlyList<FeatureViewModel> Items      { get; init; } = [];
}

public sealed class CtaBannerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/CtaBanner";
    public override string BlockClass => "sg-element--comp-cta-banner";
    public override string Alias      => "elementCompCtaBanner";

    public ContentTextModel? Text { get; init; }
    public ContentCtaModel?  Cta  { get; init; }
}

public sealed class BannerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Banner";
    public override string BlockClass => "sg-element--comp-banner";
    public override string Alias      => "elementCompBanner";

    public ContentTextModel? Text { get; init; }
    public ContentCtaModel?  Cta  { get; init; }
}

public sealed class FaqListViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FaqList";
    public override string BlockClass => "sg-element--comp-faq-list";
    public override string Alias      => "elementCompFaqList";

    public ContentCollectionModel?          Collection { get; init; }
    public IReadOnlyList<FaqItemViewModel>  Items      { get; init; } = [];
}

public sealed class TestimonialListViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TestimonialList";
    public override string BlockClass => "sg-element--comp-testimonial-list";
    public override string Alias      => "elementCompTestimonialList";

    public ContentCollectionModel?                  Collection { get; init; }
    public IReadOnlyList<TestimonialItemViewModel>  Items      { get; init; } = [];
}

public sealed class LogoCloudViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LogoCloud";
    public override string BlockClass => "sg-element--comp-logo-cloud";
    public override string Alias      => "elementCompLogoCloud";

    public ContentCollectionModel?           Collection { get; init; }
    public ContentTextModel?                 Text       { get; init; }
    public IReadOnlyList<LogoItemViewModel>  Items      { get; init; } = [];
}

public sealed class MediaTextSplitViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MediaTextSplit";
    public override string BlockClass => "sg-element--comp-media-text-split";
    public override string Alias      => "elementCompMediaTextSplit";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}

public sealed class InfoBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/InfoBlock";
    public override string BlockClass => "sg-element--comp-info-block";
    public override string Alias      => "elementCompInfoBlock";

    public ContentTextModel? Text { get; init; }
    public ContentCtaModel?  Cta  { get; init; }
}

public sealed class AccordionItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/AccordionItem";
    public override string BlockClass => "sg-element--comp-accordion-item";
    public override string Alias      => "elementCompAccordionItem";

    /// <summary>Title = panel header; Body = panel content.</summary>
    public ContentTextModel? Text { get; init; }
}

public sealed class AccordionViewModel : BaseComponentViewModel, IHasCollection, IHasText
{
    public override string ViewName   => "Partials/Ssr/Components/Accordion";
    public override string BlockClass => "sg-element--comp-accordion";
    public override string Alias      => "elementCompAccordion";

    public ContentCollectionModel?               Collection { get; init; }
    public ContentTextModel?                     Text       { get; init; }
    public IReadOnlyList<AccordionItemViewModel> Items      { get; init; } = [];
}
