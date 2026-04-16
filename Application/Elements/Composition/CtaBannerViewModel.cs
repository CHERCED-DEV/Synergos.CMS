using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class CtaBannerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/CtaBanner";
    public override string BlockClass => "sg-element--comp-cta-banner";
    public override string Alias      => "elementCompCtaBanner";

    public ContentTextModel? Text { get; init; }
    public ContentCtaModel?  Cta  { get; init; }
}
