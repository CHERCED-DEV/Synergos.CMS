using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class MissionBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MissionBlock";
    public override string BlockClass => "sg-element--corp-mission-block";
    public override string Alias      => "elementCorpMissionBlock";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
}
