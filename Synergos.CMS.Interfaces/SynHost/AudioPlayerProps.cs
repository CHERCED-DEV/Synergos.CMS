namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-audio-player&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba la URL del audio como
/// <c>audioUrl</c> y el elemento lee <c>audioFile</c>: el reproductor colocado salía en su estado
/// vacío, sin fuente, con el título y el artista del editor encima de nada.</para>
///
/// <para><b><c>audioFile</c> es la URL absoluta del medio</b> que el editor eligió en el
/// MediaPicker3 (la misma que calculaba la vista); sin medio, o con un medio sin fichero, no
/// viaja.</para>
///
/// <para><c>autoplay</c>, <c>loop</c> y <c>preload</c> los acepta el elemento y no los autora el
/// ElementType: quedan como atributos del elemento, no viajan en el <c>config</c>.</para>
///
/// <para><b>Secciones <c>Media</c> y <c>Audio</c></b> (ADR 0136). <c>Media</c> es el transporte que
/// comparte con <c>video-player</c> (una clave por control, no dos copias de «Pausar»);
/// <c>Audio</c>, su nombre accesible y su estado vacío. El título y el artista son contenido del
/// editor: no pasan por el diccionario.</para>
/// </remarks>
[ElementoSynHost("audio-player", TipoDeColocable.Pieza, Diccionario = ["Media", "Audio"])]
public sealed record AudioPlayerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? AudioFile,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? TrackTitle,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? ArtistName);
