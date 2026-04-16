using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class FeatureGridViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FeatureGrid";
    public override string BlockClass => "sg-element--comp-feature-grid";
    public override string Alias      => "elementCompFeatureGrid";

    public ContentCollectionModel?         Collection { get; init; }
    public IReadOnlyList<FeatureViewModel> Items      { get; init; } = [];
}
