using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// ViewModel for the FormEmbed element.
/// Resolves a FormDefinition content picker into a renderable form model.
/// </summary>
public sealed class FormEmbedViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FormEmbed";
    public override string BlockClass => "sg-element--form-embed";
    public override string Alias      => "elementFormEmbed";

    /// <summary>Resolved form definition ready for rendering.</summary>
    public FormDefinitionModel? Form { get; init; }

    /// <summary>Optional heading above the form.</summary>
    public string? Heading { get; init; }

    /// <summary>Optional subheading above the form.</summary>
    public string? Subheading { get; init; }
}
