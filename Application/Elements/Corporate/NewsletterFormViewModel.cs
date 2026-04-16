using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Behavior;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class NewsletterFormViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/NewsletterForm";
    public override string BlockClass => "sg-element--corp-newsletter-form";
    public override string Alias      => "elementCorpNewsletterForm";

    public ContentTextModel?    Text  { get; init; }
    public ContentCtaModel?     Cta   { get; init; }
    public BehaviorAsyncModel?  Async { get; init; }
}
