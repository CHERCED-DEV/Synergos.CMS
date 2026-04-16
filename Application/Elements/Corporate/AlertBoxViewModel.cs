using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class AlertBoxViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/AlertBox";
    public override string BlockClass => "sg-element--corp-alert-box";
    public override string Alias      => "elementCorpAlertBox";

    public ContentTextModel? Text        { get; init; }
    public string?           AlertType   { get; init; }
    public bool?             Dismissible { get; init; }
}
