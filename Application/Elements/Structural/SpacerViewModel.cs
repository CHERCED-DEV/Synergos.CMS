using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class SpacerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Spacer";
    public override string BlockClass => "sg-element--struct-spacer";
    public override string Alias      => "elementStructSpacer";
}
