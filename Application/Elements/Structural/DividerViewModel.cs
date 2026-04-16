using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class DividerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Divider";
    public override string BlockClass => "sg-element--struct-divider";
    public override string Alias      => "elementStructDivider";
}
