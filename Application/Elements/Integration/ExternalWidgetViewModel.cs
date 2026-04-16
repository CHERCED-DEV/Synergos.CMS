using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Behavior;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ExternalWidgetViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ExternalWidget";
    public override string BlockClass => "sg-element--int-external-widget";
    public override string Alias      => "elementIntExternalWidget";

    public ContentEmbedModel?  Embed { get; init; }
    public BehaviorAsyncModel? Async { get; init; }
}
