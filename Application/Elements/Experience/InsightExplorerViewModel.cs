using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class InsightExplorerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/InsightExplorer";
    public override string BlockClass => "sg-element--exp-insight-explorer";
    public override string Alias      => "experienceInsightExplorer";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}
