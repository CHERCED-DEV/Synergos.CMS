using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class FilterBoardViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FilterBoard";
    public override string BlockClass => "sg-element--exp-filter-board";
    public override string Alias      => "experienceFilterBoard";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
    public ContentMetadataModel?   Metadata   { get; init; }
}
