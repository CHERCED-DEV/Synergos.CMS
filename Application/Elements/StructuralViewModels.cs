using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

// ── Structural Elements ────────────────────────────────────────────────────
// Pure layout containers. No content properties — all configuration lives
// in the DOM compositions (DomLayout, DomSpacing, DomClass, DomAttributes).

public sealed class SectionViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Section";
    public override string BlockClass => "sg-element--struct-section";
    public override string Alias      => "elementStructSection";
}

public sealed class ContainerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Container";
    public override string BlockClass => "sg-element--struct-container";
    public override string Alias      => "elementStructContainer";
}

public sealed class GridViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Grid";
    public override string BlockClass => "sg-element--struct-grid";
    public override string Alias      => "elementStructGrid";
}

public sealed class ColumnViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Column";
    public override string BlockClass => "sg-element--struct-column";
    public override string Alias      => "elementStructColumn";
}

public sealed class StackViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Stack";
    public override string BlockClass => "sg-element--struct-stack";
    public override string Alias      => "elementStructStack";
}

public sealed class DividerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Divider";
    public override string BlockClass => "sg-element--struct-divider";
    public override string Alias      => "elementStructDivider";
}

public sealed class SpacerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Spacer";
    public override string BlockClass => "sg-element--struct-spacer";
    public override string Alias      => "elementStructSpacer";
}

public sealed class LayoutPreset1ColViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset1Col";
    public override string BlockClass => "sg-layout sg-layout--1col";
    public override string Alias      => "layoutPreset1Col";
}

public sealed class LayoutPreset2ColEqualViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset2ColEqual";
    public override string BlockClass => "sg-layout sg-layout--2col-equal";
    public override string Alias      => "layoutPreset2ColEqual";
}

public sealed class LayoutPreset3ColEqualViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset3ColEqual";
    public override string BlockClass => "sg-layout sg-layout--3col-equal";
    public override string Alias      => "layoutPreset3ColEqual";
}

public sealed class LayoutPreset4ColEqualViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPreset4ColEqual";
    public override string BlockClass => "sg-layout sg-layout--4col-equal";
    public override string Alias      => "layoutPreset4ColEqual";
}

public sealed class LayoutPresetMainSidebarViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LayoutPresetMainSidebar";
    public override string BlockClass => "sg-layout sg-layout--main-sidebar";
    public override string Alias      => "layoutPresetMainSidebar";
}
