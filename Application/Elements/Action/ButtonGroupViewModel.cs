using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ButtonGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/ButtonGroup";
    public override string BlockClass => "sg-element--action-button-group";
    public override string Alias      => "elementActionButtonGroup";

    /// <summary>Layout properties: alignment, direction, gap.</summary>
    public ContentCollectionModel? Collection { get; init; }
}
