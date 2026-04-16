using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class EyebrowViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Eyebrow";
    public override string BlockClass => "sg-element--text-eyebrow";
    public override string Alias      => "elementTextEyebrow";

    public ContentTextModel? Text { get; init; }
}
