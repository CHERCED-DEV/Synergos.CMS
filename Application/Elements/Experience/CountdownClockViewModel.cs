using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class CountdownClockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/CountdownClock";
    public override string BlockClass => "sg-element--exp-countdown-clock";
    public override string Alias      => "experienceCountdownClock";

    public ContentTextModel?     Text     { get; init; }
    public ContentMetadataModel? Metadata { get; init; }
}
