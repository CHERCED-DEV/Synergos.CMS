using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class NotificationStackViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/NotificationStack";
    public override string BlockClass => "sg-element--exp-notification-stack";
    public override string Alias      => "experienceNotificationStack";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}
