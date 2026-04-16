using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class TestimonialItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TestimonialItem";
    public override string BlockClass => "sg-element--info-testimonial-item";
    public override string Alias      => "elementInfoTestimonialItem";

    /// <summary>Body = quote; Caption = author attribution.</summary>
    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentBadgeModel? Badge { get; init; }
}
