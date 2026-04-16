using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class LayoutPreset1ColViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset1Col";
    public override string BlockClass => "sg-layout sg-layout--1col";
    public override string Alias      => "layoutPreset1Col";
}
