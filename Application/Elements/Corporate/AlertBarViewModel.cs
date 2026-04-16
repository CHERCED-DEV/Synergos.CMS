using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class AlertBarViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/AlertBar";
    public override string BlockClass => "sg-element--corp-alert-bar";
    public override string Alias      => "elementCorpAlertBar";

    public ContentTextModel? Text        { get; init; }
    public ContentCtaModel?  Cta         { get; init; }
    public bool?             Dismissible { get; init; }
}
