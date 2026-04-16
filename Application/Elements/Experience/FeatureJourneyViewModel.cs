using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class FeatureJourneyViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FeatureJourney";
    public override string BlockClass => "sg-element--exp-feature-journey";
    public override string Alias      => "experienceFeatureJourney";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}
