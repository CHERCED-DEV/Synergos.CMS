using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class LayoutPreset3ColEqualViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset3ColEqual";
    public override string BlockClass => "sg-layout sg-layout--3col-equal";
    public override string Alias      => "layoutPreset3ColEqual";
}
