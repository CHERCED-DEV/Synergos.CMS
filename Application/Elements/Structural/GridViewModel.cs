using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class GridViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Grid";
    public override string BlockClass => "sg-element--struct-grid";
    public override string Alias      => "elementStructGrid";
}
