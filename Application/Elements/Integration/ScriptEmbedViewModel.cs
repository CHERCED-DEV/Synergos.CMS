using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Behavior;

namespace Synergos.CMS.Application.Elements;

public sealed class ScriptEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ScriptEmbed";
    public override string BlockClass => "sg-element--int-script-embed";
    public override string Alias      => "elementIntScriptEmbed";

    public BehaviorScriptModel? Script { get; init; }
}
