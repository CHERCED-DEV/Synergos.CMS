using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ButtonViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Button";
    public override string BlockClass => "sg-element--action-button";
    public override string Alias      => "elementActionButton";

    public ContentCtaModel? Cta { get; init; }
}

public sealed class LinkViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Link";
    public override string BlockClass => "sg-element--action-link";
    public override string Alias      => "elementActionLink";

    public ContentCtaModel? Cta { get; init; }
}

public sealed class CtaGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/CtaGroup";
    public override string BlockClass => "sg-element--action-cta-group";
    public override string Alias      => "elementActionCtaGroup";

    public ContentCtaModel?       PrimaryCta   { get; init; }
    public ContentCtaModel?       SecondaryCta { get; init; }
    public ContentCollectionModel? Collection   { get; init; }
}

public sealed class ButtonGroupViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/ButtonGroup";
    public override string BlockClass => "sg-element--action-button-group";
    public override string Alias      => "elementActionButtonGroup";

    /// <summary>Layout properties: alignment, direction, gap.</summary>
    public ContentCollectionModel? Collection { get; init; }
}

public sealed class ButtonContainerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/ButtonContainer";
    public override string BlockClass => "sg-element--action-button-container";
    public override string Alias      => "elementActionButtonContainer";

    public string? Label  { get; init; }
    public string? Href   { get; init; }
    public string? Target { get; init; }
}
