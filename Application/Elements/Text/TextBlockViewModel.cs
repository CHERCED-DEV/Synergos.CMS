using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class TextBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/TextBlock";
    public override string BlockClass => "sg-element--text-block";
    public override string Alias      => "elementTextBlock";

    /// <summary>Title + body copy rendered as a self-contained text block.</summary>
    public ContentTextModel? Text { get; init; }
}
