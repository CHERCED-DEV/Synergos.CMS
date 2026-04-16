using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class LayoutPreset2ColEqualViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset2ColEqual";
    public override string BlockClass => "sg-layout sg-layout--2col-equal";
    public override string Alias      => "layoutPreset2ColEqual";
}
