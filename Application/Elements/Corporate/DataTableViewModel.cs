using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class DataTableViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/DataTable";
    public override string BlockClass => "sg-element--corp-data-table";
    public override string Alias      => "elementCorpDataTable";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}
