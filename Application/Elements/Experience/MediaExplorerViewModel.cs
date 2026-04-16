using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class MediaExplorerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MediaExplorer";
    public override string BlockClass => "sg-element--exp-media-explorer";
    public override string Alias      => "experienceMediaExplorer";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
    public ContentMetadataModel?   Metadata   { get; init; }
}
