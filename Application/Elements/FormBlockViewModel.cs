using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class FormBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FormBlock";
    public override string BlockClass => "sg-element--comp-form-block";
    public override string Alias      => "elementCompFormBlock";

    public string? FormAlias      { get; init; }
    public string? FormTitle      { get; init; }
    public string? SubmitLabel    { get; init; }
    public string? SuccessMessage { get; init; }
}
