using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class MapEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MapEmbed";
    public override string BlockClass => "sg-element--corp-map-embed";
    public override string Alias      => "elementCorpMapEmbed";

    public ContentEmbedModel?    Embed    { get; init; }
    public ContentLocationModel? Location { get; init; }
}
