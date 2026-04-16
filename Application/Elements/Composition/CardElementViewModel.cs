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
