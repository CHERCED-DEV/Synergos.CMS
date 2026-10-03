namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-stepper&gt;</c>, el indicador de pasos.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>stepsJson</c> —el TEXTO del
/// TextArea— y <c>currentStep</c> como texto; el elemento lee <c>steps</c>, una LISTA, y
/// <c>currentStep</c> sólo si es un NÚMERO: el indicador colocado salía vacío y sin paso activo.
/// </para>
///
/// <para>Cada paso lleva lo que documenta el ElementType, <c>label</c>, y viaja como <c>title</c>,
/// que es lo que el elemento pinta. El elemento sabe además pintar una <c>description</c> por paso
/// y usar un <c>id</c> en su evento, pero el ElementType no los ofrece: no viajan. La orientación
/// y si el visitante puede saltar de paso (<c>linear</c>) no los autora el editor: son atributos.
/// </para>
///
/// <para><b>Qué NO decide este record</b>: el nombre. <c>stepper</c> son dos conceptos con un
/// nombre —este indicador y el <c>+/-</c> numérico del design system— y se renombra el que cuesta
/// menos (ADR 0134 §4, pendiente). El record se ata al nombre del registry: si se renombra el
/// elemento, cambia esta línea junto con el registry y la vista; si se renombra la pieza del
/// design system, nada de acá.</para>
///
/// <para><b>Sección <c>Stepper</c></b> (ADR 0136): «Paso {n} de {total}», el nombre de cada paso
/// con su estado y el del indicador, con marcadores con nombre —el orden de las palabras es del
/// idioma—. El título de cada paso es contenido y viaja como marcador.</para>
/// </remarks>
[ElementoSynHost("stepper", TipoDeColocable.Pieza, Diccionario = ["Stepper"])]
public sealed record StepperProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<StepperItem>? Steps,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? CurrentStep);

/// <summary>Un paso: el texto que se pinta junto a su número.</summary>
public sealed record StepperItem(string Title, string? Description = null, string? Id = null);
