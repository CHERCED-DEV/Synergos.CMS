using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Behavior;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class TabGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/TabGroup";
    public override string BlockClass => "sg-element--corp-tab-group";
    public override string Alias      => "elementCorpTabGroup";

    public ContentCollectionModel? Collection { get; init; }
    public ContentTextModel?       Text       { get; init; }
}

public sealed class AlertBarViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/AlertBar";
    public override string BlockClass => "sg-element--corp-alert-bar";
    public override string Alias      => "elementCorpAlertBar";

    public ContentTextModel? Text        { get; init; }
    public ContentCtaModel?  Cta         { get; init; }
    public bool?             Dismissible { get; init; }
}

public sealed class AlertBoxViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/AlertBox";
    public override string BlockClass => "sg-element--corp-alert-box";
    public override string Alias      => "elementCorpAlertBox";

    public ContentTextModel? Text        { get; init; }
    public string?           AlertType   { get; init; }
    public bool?             Dismissible { get; init; }
}

public sealed class BannerSlideViewModel
{
    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
    public ContentCtaModel?   Cta   { get; init; }
}

public sealed class BannerSliderViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/BannerSlider";
    public override string BlockClass => "sg-element--corp-banner-slider";
    public override string Alias      => "elementCorpBannerSlider";

    public ContentCollectionModel?           Collection { get; init; }
    public ContentTextModel?                 Text       { get; init; }
    public IReadOnlyList<BannerSlideViewModel> Slides   { get; init; } = [];
}

public sealed class NewsletterFormViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/NewsletterForm";
    public override string BlockClass => "sg-element--corp-newsletter-form";
    public override string Alias      => "elementCorpNewsletterForm";

    public ContentTextModel?    Text  { get; init; }
    public ContentCtaModel?     Cta   { get; init; }
    public BehaviorAsyncModel?  Async { get; init; }
}

public sealed class SocialShareViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/SocialShare";
    public override string BlockClass => "sg-element--corp-social-share";
    public override string Alias      => "elementCorpSocialShare";

    public ContentCollectionModel? Collection { get; init; }
}

public sealed class DataTableViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/DataTable";
    public override string BlockClass => "sg-element--corp-data-table";
    public override string Alias      => "elementCorpDataTable";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}

public sealed class ContactInfoViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ContactInfo";
    public override string BlockClass => "sg-element--corp-contact-info";
    public override string Alias      => "elementCorpContactInfo";

    public ContentTextModel?     Text     { get; init; }
    public ContentCtaModel?      Cta      { get; init; }
    public ContentLocationModel? Location { get; init; }
}

public sealed class MapEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MapEmbed";
    public override string BlockClass => "sg-element--corp-map-embed";
    public override string Alias      => "elementCorpMapEmbed";

    public ContentEmbedModel?    Embed    { get; init; }
    public ContentLocationModel? Location { get; init; }
}

public sealed class MissionBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MissionBlock";
    public override string BlockClass => "sg-element--corp-mission-block";
    public override string Alias      => "elementCorpMissionBlock";

    public ContentTextModel?  Text  { get; init; }
    public ContentMediaModel? Media { get; init; }
}
