using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="VideoPlayerResolutor"/>: el video y su póster llegan como <c>videoFile</c> y
/// <c>posterImage</c> —la vista mandaba <c>videoUrl</c>/<c>posterUrl</c> y el reproductor no tenía
/// fuente (D1)— y lo que el elemento no implementa no viaja.
/// </summary>
public sealed class VideoPlayerResolutorTests
{
    private readonly ILogger<VideoPlayerResolutor> _log = Substitute.For<ILogger<VideoPlayerResolutor>>();

    private VideoPlayerResolutor Resolutor() => new(ElementoFalso.Fallback, ElementoFalso.Urls(), _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_video_y_el_poster_viajan_como_urls_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("videoFile", ElementoFalso.Medio("/media/recorrido.mp4")),
            ("posterImage", ElementoFalso.Medio("/media/recorrido.jpg", "Fachada")))).Props);

        Assert.Equal(new[] { "videoFile", "posterImage" }, cable.Keys);
        Assert.Equal("/media/recorrido.mp4", cable["videoFile"]!.ToString());
        Assert.Equal("/media/recorrido.jpg", cable["posterImage"]!.ToString());
    }

    [Fact]
    public void Capitulos_y_analitica_no_viajan_porque_el_elemento_no_los_implementa()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("videoFile", ElementoFalso.Medio("/media/recorrido.mp4")),
            ("chaptersJson", """[{"startSeconds":0,"title":"Intro"}]"""),
            ("enableAnalytics", true))).Props);

        Assert.Equal(new[] { "videoFile" }, cable.Keys);
    }

    [Fact]
    public void Un_poster_sin_fichero_no_viaja_y_se_anota_sin_tumbar_el_video()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("videoFile", ElementoFalso.Medio("/media/recorrido.mp4")),
            ("posterImage", ElementoFalso.Medio("")))).Props;

        Assert.Equal(new VideoPlayerProps("/media/recorrido.mp4", null), props);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("videoFile", ElementoFalso.Medio("/media/v.mp4")));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
