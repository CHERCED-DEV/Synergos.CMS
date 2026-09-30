namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-color-swatches&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>swatchesJson</c> —el TEXTO del
/// TextArea, con <c>hex</c>/<c>name</c> por muestra— y el elemento lee <c>swatches</c>, una LISTA
/// de <c>color</c>/<c>label</c>: la paleta colocada hidrataba sin una sola muestra.</para>
///
/// <para><c>shape</c> viaja tal cual el editor lo eligió. El DataType ofrece
/// <c>swatch/chip/dot/circle</c> y el elemento pinta <c>square/circle/pill</c>: sólo
/// <c>circle</c> coincide y lo demás cae al default del elemento (<c>circle</c>). Es schema, no
/// se traduce acá: la lista de formas vive en el UI.</para>
///
/// <para>El título, las columnas, la muestra seleccionada al inicio y si se puede deseleccionar los
/// sabe pintar el elemento pero el ElementType no los autora: quedan como atributo.</para>
/// </remarks>
[ElementoSynHost("color-swatches", TipoDeColocable.Pieza)]
public sealed record ColorSwatchesProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<ColorSwatchesItem>? Swatches,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Shape);

/// <summary>Una muestra de color: el color CSS y su nombre.</summary>
public sealed record ColorSwatchesItem(string Color, string? Label = null);
