namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-color-picker&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba la paleta como el TEXTO
/// <c>paletteJson</c> y el elemento lee la LISTA <c>palette</c> en el <c>config</c>
/// (<c>paletteJson</c> sólo lo acepta como atributo): el selector colocado mostraba la paleta de
/// fábrica, no los colores del editor. <c>label</c> e <c>initialColor</c> sí llegaban.</para>
///
/// <para><b>Cada color de la paleta es un hex</b> (<c>#rgb</c> o <c>#rrggbb</c>, con o sin
/// <c>#</c>): los que no lo son no viajan y se anotan, igual que un <c>initialColor</c> que no sea
/// un hex. Viajan como los escribió el editor; el elemento los normaliza a <c>#rrggbb</c>.</para>
///
/// <para><b>Su microcopia sale del diccionario, sección <c>ColorPicker</c></b> (ADR 0136): el rótulo por
/// defecto, el del campo hex y su error. <b>No comparte sección con <c>color-swatches</c></b>, aunque
/// los dos elijan un color: no tienen ni un texto con la misma intención, y una sección común publicaría
/// en cada página las claves del otro.</para>
/// </remarks>
[ElementoSynHost("color-picker", TipoDeColocable.Pieza, Diccionario = ["ColorPicker"])]
public sealed record ColorPickerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? InitialColor,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<string>? Palette);
