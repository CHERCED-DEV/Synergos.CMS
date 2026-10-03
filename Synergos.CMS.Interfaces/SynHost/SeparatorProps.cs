namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-separator&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>Pasa a record por #192, caso 2</b>: era la última pieza con selector que todavía se
/// emitía con un diccionario libre en la vista, así que su <c>style</c> no estaba en el contrato y
/// el gate de vocabulario (#181) no lo podía cruzar.</para>
///
/// <para><c>style</c> es el valor del selector <c>DTSelectSeparatorStyle</c> (obligatorio en el
/// ElementType). La decisión de #192 es que el elemento APRENDE solid, dashed, dotted y gradient;
/// hoy no lee la clave y pinta siempre la línea continua. La lista vive en el UI: copiarla acá
/// sería otra copia sin cruzar.</para>
/// </remarks>
[ElementoSynHost("separator", TipoDeColocable.Pieza)]
public sealed record SeparatorProps(
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Style);
