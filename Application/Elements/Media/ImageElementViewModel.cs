using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ImageElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Image";
    public override string BlockClass => "sg-element--media-image";
    public override string Alias      => "elementMediaImage";

    public ContentMediaModel? Media { get; init; }
}
