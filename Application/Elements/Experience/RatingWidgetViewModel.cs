using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class RatingWidgetViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/RatingWidget";
    public override string BlockClass => "sg-element--exp-rating-widget";
    public override string Alias      => "experienceRatingWidget";

    public ContentTextModel?     Text     { get; init; }
    public ContentMetadataModel? Metadata { get; init; }
}
