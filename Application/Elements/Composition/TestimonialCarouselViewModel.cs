using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>Carrusel de testimonios — un solo script para N TestimonialItem.</summary>
public sealed class TestimonialCarouselViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TestimonialCarousel";
    public override string BlockClass => "sg-element--comp-testimonial-carousel";
    public override string Alias      => "elementCompTestimonialCarousel";

    public ContentTextModel?                       Text    { get; init; }
    public IReadOnlyList<TestimonialItemViewModel> Items   { get; init; } = [];
    public int?                                    Columns { get; init; }
    public string?                                 Gap     { get; init; }
}
