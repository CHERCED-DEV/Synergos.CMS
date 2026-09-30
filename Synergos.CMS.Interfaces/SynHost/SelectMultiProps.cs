namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-select-multi&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>optionsJson</c> —el TEXTO que el
/// editor escribe en un TextArea— y el elemento lee <c>options</c>, una LISTA: todo multiselector
/// colocado salía sin una sola opción que elegir. Ahora el resolver parsea el texto en el servidor
/// y viaja la lista tipada. <c>maxSelections</c> viaja como número («0 = ilimitado», que es también
/// lo que el elemento hace por defecto).</para>
///
/// <para>Cada opción lleva lo que documenta el ElementType, <c>value</c> y <c>label</c>. El elemento
/// sabe además deshabilitar una (<c>disabled</c>), pero el ElementType no lo ofrece: no viaja. El
/// marcador y el texto de «sin coincidencias» no los autora el editor: son atributos.</para>
/// </remarks>
[ElementoSynHost("select-multi", TipoDeColocable.Pieza)]
public sealed record SelectMultiProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<SelectMultiItem>? Options,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? MaxSelections);

/// <summary>Una opción: el valor que se emite al elegirla y el texto que se pinta.</summary>
public sealed record SelectMultiItem(string Value, string Label);
