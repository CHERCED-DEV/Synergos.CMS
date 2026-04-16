using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class MacroHostViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Integration/MacroHost";
    public override string BlockClass => "sg-element--int-macro-host";
    public override string Alias      => "elementIntMacroHost";

    public string? MacroAlias     { get; init; }
    public string? MacroTitle     { get; init; }
    public string? MacroVariant   { get; init; }
    public string? MacroTheme     { get; init; }
    public string? MacroElementId { get; init; }
    public string? MacroParams    { get; init; }
}
