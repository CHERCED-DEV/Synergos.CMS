using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class LabelViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Label";
    public override string BlockClass => "sg-element--text-label";
    public override string Alias      => "elementTextLabel";

    public ContentTextModel? Text { get; init; }
}
