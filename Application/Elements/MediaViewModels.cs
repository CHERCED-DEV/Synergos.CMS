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

public sealed class VideoElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Video";
    public override string BlockClass => "sg-element--media-video";
    public override string Alias      => "elementMediaVideo";

    public ContentEmbedModel? Embed { get; init; }
}

public sealed class IconViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Icon";
    public override string BlockClass => "sg-element--media-icon";
    public override string Alias      => "elementMediaIcon";

    public ContentMediaModel?  Media { get; init; }
    public ContentBadgeModel?  Badge { get; init; }
}

public sealed class GalleryItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/GalleryItem";
    public override string BlockClass => "sg-element--media-gallery-item";
    public override string Alias      => "elementMediaGalleryItem";

    public ContentMediaModel? Media { get; init; }
    public ContentTextModel?  Text  { get; init; }
}

public sealed class LogoItemViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/LogoItem";
    public override string BlockClass => "sg-element--media-logo-item";
    public override string Alias      => "elementMediaLogoItem";

    public ContentMediaModel? Media { get; init; }
    public ContentTextModel?  Text  { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}

public sealed class AvatarViewModel : BaseComponentViewModel, IHasMedia, IHasText
{
    public override string ViewName   => "Partials/Ssr/Foundation/Avatar";
    public override string BlockClass => "sg-element--media-avatar";
    public override string Alias      => "elementMediaAvatar";

    public ContentMediaModel? Media { get; init; }
    public ContentTextModel?  Text  { get; init; }
}
