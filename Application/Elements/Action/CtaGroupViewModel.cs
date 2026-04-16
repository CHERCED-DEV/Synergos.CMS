using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class CtaGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/CtaGroup";
    public override string BlockClass => "sg-element--action-cta-group";
    public override string Alias      => "elementActionCtaGroup";

    public ContentCtaModel?       PrimaryCta   { get; init; }
    public ContentCtaModel?       SecondaryCta { get; init; }
    public ContentCollectionModel? Collection   { get; init; }
}
