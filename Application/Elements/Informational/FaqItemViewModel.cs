using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class FaqItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FaqItem";
    public override string BlockClass => "sg-element--info-faq-item";
    public override string Alias      => "elementInfoFaqItem";

    /// <summary>Title = question; Body = answer.</summary>
    public ContentTextModel? Text { get; init; }
}
