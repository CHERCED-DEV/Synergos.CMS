using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class BadgeViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Badge";
    public override string BlockClass => "sg-element--info-badge";
    public override string Alias      => "elementInfoBadge";

    public ContentBadgeModel? Badge { get; init; }
}
