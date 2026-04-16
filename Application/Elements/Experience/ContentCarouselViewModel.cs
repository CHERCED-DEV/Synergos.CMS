using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ContentCarouselViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ContentCarousel";
    public override string BlockClass => "sg-element--exp-content-carousel";
    public override string Alias      => "experienceContentCarousel";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
    public ContentMetadataModel?   Metadata   { get; init; }
}
