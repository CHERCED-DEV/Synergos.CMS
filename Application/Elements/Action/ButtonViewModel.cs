using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ButtonViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Button";
    public override string BlockClass => "sg-element--action-button";
    public override string Alias      => "elementActionButton";

    public ContentCtaModel? Cta { get; init; }
}
