using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ParagraphViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Paragraph";
    public override string BlockClass => "sg-element--text-paragraph";
    public override string Alias      => "elementTextParagraph";

    public ContentTextModel? Text { get; init; }
}
