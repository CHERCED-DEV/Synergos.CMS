using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class RichTextElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/RichText";
    public override string BlockClass => "sg-element--text-richtext";
    public override string Alias      => "elementTextRichText";

    public ContentTextModel? Text { get; init; }
}
