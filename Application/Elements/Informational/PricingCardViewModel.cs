using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class PricingCardViewModel : BaseComponentViewModel, IHasText, IHasCta, IHasBadge
{
    public override string ViewName   => "Partials/Ssr/Components/PricingCard";
    public override string BlockClass => "sg-element--info-pricing-card";
    public override string Alias      => "elementInfoPricingCard";

    public ContentTextModel?    Text    { get; init; }
    public ContentCtaModel?     Cta     { get; init; }
    public ContentBadgeModel?   Badge   { get; init; }
    public ContentPricingModel? Pricing { get; init; }
}
