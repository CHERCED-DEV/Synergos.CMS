using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class AvatarViewModel : BaseComponentViewModel, IHasMedia, IHasText
{
    public override string ViewName   => "Partials/Ssr/Foundation/Avatar";
    public override string BlockClass => "sg-element--media-avatar";
    public override string Alias      => "elementMediaAvatar";

    public ContentMediaModel? Media { get; init; }
    public ContentTextModel?  Text  { get; init; }
}
