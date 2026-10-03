namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-tour-guide&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>stepsJson</c> —el TEXTO del
/// TextArea— y el elemento lee <c>steps</c>, una LISTA: el recorrido colocado no tenía pasos y no
/// arrancaba nunca, aunque el editor encendiera <c>autoStart</c> (que sí llegaba).</para>
///
/// <para>Cada paso lleva lo que documenta el ElementType con el nombre que el elemento lee:
/// <c>selector</c> viaja como <c>target</c> (el elemento de la página que se ilumina), <c>title</c>
/// y <c>content</c> como <c>body</c>. Con el TEXTO el elemento tampoco habría encontrado
/// <c>content</c>: lee <c>body</c> o <c>text</c>. La posición del globo por paso la sabe leer el
/// elemento pero el ElementType no la ofrece: no viaja. Los rótulos de los botones (siguiente,
/// anterior, saltar, finalizar) no los autora el editor: son atributos.</para>
///
/// <para><c>autoStart</c> sólo viaja ENCENDIDO: apagado es el default del elemento.</para>
///
/// <para><b>Sus rótulos por defecto salen del diccionario</b> (ADR 0136): «Siguiente» y «Anterior» son
/// <c>Common.Actions.Next</c>/<c>Previous</c>, la misma acción genérica del resto del sitio (la
/// sección se publica entera: 16 claves para dos), y «Saltar»/«Finalizar», la sección
/// <c>TourGuide</c>.</para>
/// </remarks>
[ElementoSynHost("tour-guide", TipoDeColocable.Pieza, Diccionario = ["TourGuide", "Common.Actions"])]
public sealed record TourGuideProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<TourGuideStep>? Steps,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? AutoStart);

/// <summary>Un paso del recorrido: a qué apunta (selector CSS), su título y su texto.</summary>
public sealed record TourGuideStep(string? Target = null, string? Title = null, string? Body = null, string? Placement = null);
