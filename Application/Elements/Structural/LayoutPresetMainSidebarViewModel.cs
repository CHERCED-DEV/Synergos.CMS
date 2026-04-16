using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class LayoutPresetMainSidebarViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPresetMainSidebar";
    public override string BlockClass => "sg-layout sg-layout--main-sidebar";
    public override string Alias      => "layoutPresetMainSidebar";
}
