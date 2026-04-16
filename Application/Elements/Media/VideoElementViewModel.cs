using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class VideoElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Video";
    public override string BlockClass => "sg-element--media-video";
    public override string Alias      => "elementMediaVideo";

    public ContentEmbedModel? Embed { get; init; }
}
