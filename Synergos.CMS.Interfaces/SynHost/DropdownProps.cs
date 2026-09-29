namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-dropdown&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>optionsJson</c> —el TEXTO que el
/// editor escribe en un TextArea— y el elemento lee <c>options</c>, una LISTA: todo dropdown
/// colocado salía como un botón gris sin opciones. Ahora el resolver parsea ese texto en el
/// servidor y viaja la lista tipada.</para>
///
/// <para><b>Qué NO decide este record</b>: si el dropdown sigue siendo colocable o queda como host
/// delgado de <c>syn-dropdown</c> (ADR 0134 §4, pendiente). Los GRUPOS que promete la descripción
/// del ElementType no los pinta el elemento: una entrada de grupo no viaja y se anota.</para>
/// </remarks>
[ElementoSynHost("dropdown", TipoDeColocable.Pieza)]
public sealed record DropdownProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? TriggerLabel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<DropdownOption>? Options,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? SelectedValue,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? Searchable);

/// <summary>Una opción del menú. Con <c>Href</c> es un enlace; sin él, una acción.</summary>
public sealed record DropdownOption(string Value, string Label, string? Href = null);
