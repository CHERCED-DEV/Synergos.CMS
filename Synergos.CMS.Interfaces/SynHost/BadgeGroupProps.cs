namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-badge-group&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>badgesJson</c> —el TEXTO del
/// TextArea, con <c>label</c>/<c>color</c>/<c>iconKey</c> por insignia— y el elemento lee
/// <c>badges</c>, una LISTA de <c>label</c>/<c>tone</c>: el grupo colocado hidrataba con «No hay
/// elementos.» en vez de las insignias del editor.</para>
///
/// <para><c>tone</c> es el tono del design system (<c>neutral</c>, <c>brand</c>, <c>success</c>…);
/// el ElementType lo llama <c>color</c> y lo autora como texto libre. La lista de tonos vive en el
/// UI: el elemento descarta lo que no conoce y cae a <c>neutral</c>. Lo mismo <c>layout</c>: el
/// DataType ofrece <c>inline/stack/grid/cluster</c> y el elemento pinta <c>wrap/inline/stack</c>
/// (<c>grid</c> y <c>cluster</c> caen a <c>wrap</c>; es schema, no se toca acá).</para>
///
/// <para><c>iconKey</c> lo promete la descripción del ElementType y el elemento no pinta iconos en
/// las insignias: no viaja. <c>count</c>, <c>href</c> y la selección los sabe pintar el elemento
/// pero el ElementType no los autora: quedan como atributo.</para>
///
/// <para><b>Su microcopia sale del diccionario, sección <c>BadgeGroup</c></b> (ADR 0136): el nombre del
/// grupo cuando no tiene rótulo y el grupo vacío.</para>
/// </remarks>
[ElementoSynHost("badge-group", TipoDeColocable.Pieza, Diccionario = ["BadgeGroup"])]
public sealed record BadgeGroupProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<BadgeGroupItem>? Badges,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Layout);

/// <summary>Una insignia del grupo: su texto y su tono.</summary>
public sealed record BadgeGroupItem(string Label, string? Tone = null);
