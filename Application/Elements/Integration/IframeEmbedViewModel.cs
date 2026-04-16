using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class IframeEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/IframeEmbed";
    public override string BlockClass => "sg-element--int-iframe-embed";
    public override string Alias      => "elementIntIframeEmbed";

    public ContentEmbedModel? Embed { get; init; }
}
