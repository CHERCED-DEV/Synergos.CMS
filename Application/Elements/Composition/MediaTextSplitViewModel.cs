using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class MediaTextSplitViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MediaTextSplit";
    public override string BlockClass => "sg-element--comp-media-text-split";
    public override string Alias      => "elementCompMediaTextSplit";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}
