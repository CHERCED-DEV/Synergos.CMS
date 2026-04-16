using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class IconViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Icon";
    public override string BlockClass => "sg-element--media-icon";
    public override string Alias      => "elementMediaIcon";

    public ContentMediaModel?  Media { get; init; }
    public ContentBadgeModel?  Badge { get; init; }
}
