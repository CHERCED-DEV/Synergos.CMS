using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class ColumnViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Column";
    public override string BlockClass => "sg-element--struct-column";
    public override string Alias      => "elementStructColumn";
}
