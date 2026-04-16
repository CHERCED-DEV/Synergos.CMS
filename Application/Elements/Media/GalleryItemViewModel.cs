using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class GalleryItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/GalleryItem";
    public override string BlockClass => "sg-element--media-gallery-item";
    public override string Alias      => "elementMediaGalleryItem";

    public ContentMediaModel? Media { get; init; }
    public ContentTextModel?  Text  { get; init; }
}
