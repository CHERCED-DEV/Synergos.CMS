using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Elements;

public sealed class AttributedQuoteViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/AttributedQuote";
    public override string BlockClass => "sg-element--text-attributed-quote";
    public override string Alias      => "elementTextAttributedQuote";

    public string? QuoteText   { get; init; }
    public string? QuoteAuthor { get; init; }
    public string? QuoteRole   { get; init; }
}
