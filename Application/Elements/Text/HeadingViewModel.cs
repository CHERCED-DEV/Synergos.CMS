using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class HeadingViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Heading";
    public override string BlockClass => "sg-element--text-heading";
    public override string Alias      => "elementTextHeading";

    public ContentHeadingModel? Heading { get; init; }
}
