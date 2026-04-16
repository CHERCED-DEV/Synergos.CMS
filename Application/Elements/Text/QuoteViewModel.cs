using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class QuoteViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Quote";
    public override string BlockClass => "sg-element--text-quote";
    public override string Alias      => "elementTextQuote";

    public ContentTextModel? Text { get; init; }
}
