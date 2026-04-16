using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class HeroElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Hero";
    public override string BlockClass => "sg-element--comp-hero";
    public override string Alias      => "elementCompHero";

    public ContentHeadingModel? Heading { get; init; }
    public ContentTextModel?    Text    { get; init; }
    public ContentMediaModel?   Media   { get; init; }
    public ContentCtaModel?     Cta     { get; init; }
}
