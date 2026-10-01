namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-rich-tooltip&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba el contenido del tooltip como
/// <c>tooltipContent</c> —el HTML del editor enriquecido— y el elemento lee <c>body</c>: el
/// tooltip colocado no tenía contenido, así que no abría panel ninguno; sólo quedaba el
/// disparador.</para>
///
/// <para><b><c>body</c> viaja como TEXTO PLANO.</b> El elemento lo pinta como texto, no como
/// marcado: el resolver baja el RTE a texto (sin etiquetas, entidades decodificadas). Negritas,
/// enlaces o listas del editor se pierden a propósito: pintarlas exigiría que el elemento
/// aceptara marcado saneado, que es otra decisión.</para>
///
/// <para><b><c>placement</c> es el LADO.</b> El DataType ofrece además la alineación
/// (<c>top-start</c>, <c>bottom-end</c>…) y el elemento sólo sabe de lados: sin traducir,
/// «bottom-start» caía al default y el tooltip salía ARRIBA. El resolver conserva la decisión
/// principal del editor —el lado— y suelta la alineación, que el elemento no pinta.</para>
///
/// <para><c>title</c>, <c>actionLabel</c> y <c>actionHref</c> los acepta el elemento y no los
/// autora el ElementType: quedan como atributos.</para>
///
/// <para><b>Su única microcopia</b> —el nombre del disparador cuando no tiene texto ni título— sale del
/// diccionario: <c>Common.Actions.LearnMore</c> (ADR 0136), la misma acción genérica del resto del
/// sitio. La sección se publica entera: 16 claves para una, el precio de no copiarla.</para>
/// </remarks>
[ElementoSynHost("rich-tooltip", TipoDeColocable.Pieza, Diccionario = ["Common.Actions"])]
public sealed record RichTooltipProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? TriggerText,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Body,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Placement);
