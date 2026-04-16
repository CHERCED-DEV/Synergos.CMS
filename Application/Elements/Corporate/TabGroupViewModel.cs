using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class TabGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TabGroup";
    public override string BlockClass => "sg-element--corp-tab-group";
    public override string Alias      => "elementCorpTabGroup";

    public ContentCollectionModel? Collection { get; init; }
    public ContentTextModel?       Text       { get; init; }
}
