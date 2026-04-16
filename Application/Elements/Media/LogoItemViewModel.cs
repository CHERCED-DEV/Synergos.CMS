using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class LogoItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LogoItem";
    public override string BlockClass => "sg-element--media-logo-item";
    public override string Alias      => "elementMediaLogoItem";

    public ContentMediaModel? Media { get; init; }
    public ContentTextModel?  Text  { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}
