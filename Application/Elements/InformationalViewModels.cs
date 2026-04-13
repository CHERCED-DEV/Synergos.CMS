using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class BadgeViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Badge";
    public override string BlockClass => "sg-element--info-badge";
    public override string Alias      => "elementInfoBadge";

    public ContentBadgeModel? Badge { get; init; }
}

public sealed class StatViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Stat";
    public override string BlockClass => "sg-element--info-stat";
    public override string Alias      => "elementInfoStat";

    /// <summary>Title = the number/metric; Subtitle = the label below it.</summary>
    public ContentTextModel? Text { get; init; }
}

public sealed class FeatureViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Feature";
    public override string BlockClass => "sg-element--info-feature";
    public override string Alias      => "elementInfoFeature";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}

public sealed class KeyValueViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/KeyValue";
    public override string BlockClass => "sg-element--info-key-value";
    public override string Alias      => "elementInfoKeyValue";

    /// <summary>Title = key; Summary = value.</summary>
    public ContentTextModel? Text { get; init; }
}

public sealed class TimelineItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TimelineItem";
    public override string BlockClass => "sg-element--info-timeline-item";
    public override string Alias      => "elementInfoTimelineItem";

    public ContentTextModel? Text { get; init; }
    public ContentDateModel? Date { get; init; }
}

public sealed class FaqItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FaqItem";
    public override string BlockClass => "sg-element--info-faq-item";
    public override string Alias      => "elementInfoFaqItem";

    /// <summary>Title = question; Body = answer.</summary>
    public ContentTextModel? Text { get; init; }
}

public sealed class TestimonialItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TestimonialItem";
    public override string BlockClass => "sg-element--info-testimonial-item";
    public override string Alias      => "elementInfoTestimonialItem";

    /// <summary>Body = quote; Caption = author attribution.</summary>
    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentBadgeModel? Badge { get; init; }
}

public sealed class PricingCardViewModel : BaseComponentViewModel, IHasText, IHasCta, IHasBadge
{
    public override string ViewName   => "Partials/Ssr/Components/PricingCard";
    public override string BlockClass => "sg-element--info-pricing-card";
    public override string Alias      => "elementInfoPricingCard";

    public ContentTextModel?  Text  { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
    public ContentBadgeModel? Badge { get; init; }
}
