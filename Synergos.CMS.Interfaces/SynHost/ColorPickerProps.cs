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
/// </remarks>
[ElementoSynHost("color-picker", TipoDeColocable.Pieza)]
public sealed record ColorPickerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? InitialColor,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<string>? Palette);
