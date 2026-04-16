using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class TimelineItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TimelineItem";
    public override string BlockClass => "sg-element--info-timeline-item";
    public override string Alias      => "elementInfoTimelineItem";

    public ContentTextModel? Text { get; init; }
    public ContentDateModel? Date { get; init; }
}
