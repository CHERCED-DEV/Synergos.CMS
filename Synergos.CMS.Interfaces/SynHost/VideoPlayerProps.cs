namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-video-player&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>videoUrl</c>, <c>posterUrl</c>,
/// <c>chaptersJson</c> y <c>enableAnalytics</c>; el elemento lee <c>videoFile</c> y
/// <c>posterImage</c>, y ninguna de las cuatro: el reproductor colocado no tenía fuente ni póster
/// y no pintaba un <c>&lt;video&gt;</c>.</para>
///
/// <para><b>Los dos son URLs absolutas de medios</b> (MediaPicker3 <c>videoFile</c> y
/// <c>posterImage</c>); sin medio, o con uno sin fichero, no viajan.</para>
///
/// <para><b>Lo que el ElementType promete y el elemento no pinta no viaja.</b>
/// <c>chaptersJson</c> (capítulos) y <c>enableAnalytics</c> (eventos play/25 %/…/complete) los
/// declara el ElementType y el elemento no los implementa: los recibe como atributos «inertes»
/// para que el host no falle, y no hace nada con ellos. Mandarlos sería afirmar una función que
/// no existe. <c>title</c>, <c>autoplay</c>, <c>loop</c> y <c>muted</c> los acepta el elemento y
/// no los autora el ElementType: quedan como atributos.</para>
///
/// <para><b>Secciones <c>Media</c> y <c>Video</c></b> (ADR 0136). <c>Media</c> es el transporte que
/// comparte con <c>audio-player</c> —reproducir, pausar, silenciar, volumen, posición, «x de y»—:
/// el mismo concepto en los dos reproductores, así que una clave por control y no dos copias de
/// «Pausar». <c>Video</c> (que ya existía) es lo que sólo tiene un video: pantalla completa, su
/// nombre accesible y su estado vacío.</para>
/// </remarks>
[ElementoSynHost("video-player", TipoDeColocable.Pieza, Diccionario = ["Media", "Video"])]
public sealed record VideoPlayerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? VideoFile,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? PosterImage);
