using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="AudioPlayerResolutor"/>: el medio del editor llega como <c>audioFile</c>, que es
/// lo que el elemento lee — la vista lo mandaba como <c>audioUrl</c> y el reproductor salía sin
/// fuente (D1).
/// </summary>
public sealed class AudioPlayerResolutorTests
{
    private readonly ILogger<AudioPlayerResolutor> _log = Substitute.For<ILogger<AudioPlayerResolutor>>();

    private AudioPlayerResolutor Resolutor() => new(ElementoFalso.Fallback, ElementoFalso.Urls(), _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_audio_viaja_como_la_url_del_medio_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("audioFile", ElementoFalso.Medio("/media/podcast/episodio-12.mp3")),
            ("trackTitle", "Episodio 12"),
            ("artistName", "Radio Synergos"))).Props);

        Assert.Equal(new[] { "audioFile", "trackTitle", "artistName" }, cable.Keys);
        Assert.Equal("/media/podcast/episodio-12.mp3", cable["audioFile"]!.ToString());
        Assert.False(cable.ContainsKey("audioUrl"));
    }

    [Fact]
    public void Un_medio_sin_fichero_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("audioFile", ElementoFalso.Medio("")),
            ("trackTitle", "Episodio 12"))).Props;

        Assert.Null(props.AudioFile);
        Assert.Equal("Episodio 12", props.TrackTitle);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("audioFile", ElementoFalso.Medio("/media/a.mp3")), ("trackTitle", "T"));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
