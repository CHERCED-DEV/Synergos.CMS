using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class ContainerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Container";
    public override string BlockClass => "sg-element--struct-container";
    public override string Alias      => "elementStructContainer";
}
