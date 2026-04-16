using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>Acordeón de FAQs — un solo script para N FaqItem.</summary>
public sealed class AccordionGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/AccordionGroup";
    public override string BlockClass => "sg-element--comp-accordion-group";
    public override string Alias      => "elementCompAccordionGroup";

    public ContentTextModel?               Text  { get; init; }
    public IReadOnlyList<FaqItemViewModel> Items { get; init; } = [];
}
