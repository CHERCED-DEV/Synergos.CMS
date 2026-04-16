using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class StatViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/Stat";
    public override string BlockClass => "sg-element--info-stat";
    public override string Alias      => "elementInfoStat";

    /// <summary>Title = the number/metric; Subtitle = the label below it.</summary>
    public ContentTextModel? Text { get; init; }
}
