using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class LinkViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Link";
    public override string BlockClass => "sg-element--action-link";
    public override string Alias      => "elementActionLink";

    public ContentCtaModel? Cta { get; init; }
}
