using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Behavior;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ScriptEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ScriptEmbed";
    public override string BlockClass => "sg-element--int-script-embed";
    public override string Alias      => "elementIntScriptEmbed";

    public BehaviorScriptModel? Script { get; init; }
}

public sealed class IframeEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/IframeEmbed";
    public override string BlockClass => "sg-element--int-iframe-embed";
    public override string Alias      => "elementIntIframeEmbed";

    public ContentEmbedModel? Embed { get; init; }
}

public sealed class ExternalWidgetViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ExternalWidget";
    public override string BlockClass => "sg-element--int-external-widget";
    public override string Alias      => "elementIntExternalWidget";

    public ContentEmbedModel?  Embed { get; init; }
    public BehaviorAsyncModel? Async { get; init; }
}

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
