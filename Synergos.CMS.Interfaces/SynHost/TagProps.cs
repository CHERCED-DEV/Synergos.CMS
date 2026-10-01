namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-tag&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista emitía <c>tagLabel</c> y <c>tagColor</c> y el
/// elemento lee <c>label</c> y <c>color</c> del <c>config</c>: sin etiqueta el chip no tiene qué
/// pintar y <b>desaparece</b> al hidratar.</para>
///
/// <para><c>color</c> es el tono del design system (<c>neutral</c>, <c>brand</c>, <c>success</c>…).
/// El ElementType lo autora como TEXTO libre —debería ser un selector (ADR 0134), y es cambio de
/// schema—; el elemento descarta lo que no conoce y cae a <c>neutral</c>. La lista vive en el UI:
/// copiarla acá sería otra copia sin cruzar.</para>
/// </remarks>
[ElementoSynHost("tag", TipoDeColocable.Pieza, Diccionario = ["Tag"])]
public sealed record TagProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Color);
