using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class ButtonContainerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/ButtonContainer";
    public override string BlockClass => "sg-element--action-button-container";
    public override string Alias      => "elementActionButtonContainer";

    public string? Label  { get; init; }
    public string? Href   { get; init; }
    public string? Target { get; init; }
}
