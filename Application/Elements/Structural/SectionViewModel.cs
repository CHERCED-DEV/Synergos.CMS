using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class SectionViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Section";
    public override string BlockClass => "sg-element--struct-section";
    public override string Alias      => "elementStructSection";
}
