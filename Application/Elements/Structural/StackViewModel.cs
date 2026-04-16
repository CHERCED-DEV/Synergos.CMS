using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class StackViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Stack";
    public override string BlockClass => "sg-element--struct-stack";
    public override string Alias      => "elementStructStack";
}
