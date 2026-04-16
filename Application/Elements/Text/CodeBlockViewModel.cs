using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class CodeBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/CodeBlock";
    public override string BlockClass => "sg-element--text-code-block";
    public override string Alias      => "elementTextCodeBlock";

    public string? Language { get; init; }
    public string? Content  { get; init; }
}
