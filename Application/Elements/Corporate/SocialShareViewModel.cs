using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class SocialShareViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/SocialShare";
    public override string BlockClass => "sg-element--corp-social-share";
    public override string Alias      => "elementCorpSocialShare";

    public ContentCollectionModel? Collection { get; init; }
}
