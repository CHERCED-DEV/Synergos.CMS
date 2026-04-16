using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>Individual slide inside a BannerSlider. Item-only (no base class).</summary>
public sealed class BannerSlideViewModel
{
    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}

public sealed class BannerSliderViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/BannerSlider";
    public override string BlockClass => "sg-element--corp-banner-slider";
    public override string Alias      => "elementCorpBannerSlider";

    public ContentCollectionModel?             Collection { get; init; }
    public ContentTextModel?                   Text       { get; init; }
    public IReadOnlyList<BannerSlideViewModel> Slides     { get; init; } = [];
}
