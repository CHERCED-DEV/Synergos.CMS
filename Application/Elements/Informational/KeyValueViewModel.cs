using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class KeyValueViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/KeyValue";
    public override string BlockClass => "sg-element--info-key-value";
    public override string Alias      => "elementInfoKeyValue";

    /// <summary>Title = key; Summary = value.</summary>
    public ContentTextModel? Text { get; init; }
}
