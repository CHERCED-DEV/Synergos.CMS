using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class FeatureViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Feature";
    public override string BlockClass => "sg-element--info-feature";
    public override string Alias      => "elementInfoFeature";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}
