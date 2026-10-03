using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="CarouselResolutor"/>: el TEXTO <c>slidesJson</c> (<c>imageUrl</c>/<c>alt</c>/
/// <c>caption</c>) llega como la LISTA <c>slides</c> (<c>src</c>/<c>alt</c>/<c>label</c>) que el
/// elemento lee, y el intervalo como <c>autoplay</c> + <c>interval</c> — con los alias el carrusel
/// colocado no se veía (D1).
/// </summary>
public sealed class CarouselResolutorTests
{
    private const string Diapositivas =
        """[{"imageUrl":"/media/sala.jpg","alt":"Sala","caption":"La sala"},{"imageUrl":"/media/cocina.jpg","alt":"Cocina","linkUrl":"/x"}]""";

    private readonly ILogger<CarouselResolutor> _log = Substitute.For<ILogger<CarouselResolutor>>();

    private CarouselResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_diapositivas_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("slidesJson", Diapositivas),
            ("autoplayInterval", "4000"))).Props);

        Assert.Equal(new[] { "slides", "autoplay", "interval" }, cable.Keys);
        var slides = (JsonElement)cable["slides"]!;
        Assert.Equal(2, slides.GetArrayLength());
        Assert.Equal("/media/sala.jpg", slides[0].GetProperty("src").GetString());
        Assert.Equal("La sala", slides[0].GetProperty("label").GetString());
        Assert.False(slides[1].TryGetProperty("label", out _));
        Assert.Equal("/x", slides[1].GetProperty("linkUrl").GetString());
        Assert.False(slides[0].TryGetProperty("linkUrl", out _));
        Assert.True(((JsonElement)cable["autoplay"]!).GetBoolean());
        Assert.Equal(4000, ((JsonElement)cable["interval"]!).GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("-5")]
    public void Cero_vacio_o_negativo_dejan_el_autoplay_apagado(string intervalo)
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("autoplayInterval", intervalo))).Props;

        Assert.Null(props.Autoplay);
        Assert.Null(props.Interval);
    }

    [Fact]
    public void Una_diapositiva_sin_imagen_no_viaja_y_se_anota()
    {
        var slides = Resolutor().Resolver(ElementoFalso.Con(
            ("slidesJson", """[{"alt":"sin imagen"},{"imageUrl":"/a.jpg"}]"""))).Props.Slides;

        Assert.Equal(new[] { new CarouselSlide("/a.jpg") }, slides);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("slidesJson", Diapositivas));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Slides, Resolutor().Resolver(elemento).Props.Slides);
    }

    // #192, caso 4: el elemento enlaza cada diapositiva y el record descartaba linkUrl.
    [Fact]
    public void El_enlace_de_una_diapositiva_viaja_y_la_que_no_lo_trae_no_enlaza()
    {
        var slides = Resolutor().Resolver(ElementoFalso.Con(
            ("slidesJson", """[{"imageUrl":"/a.jpg","linkUrl":"/propiedades/101"},{"imageUrl":"/b.jpg","linkUrl":"  "}]"""))).Props.Slides!;

        Assert.Equal(new string?[] { "/propiedades/101", null }, slides.Select(s => s.LinkUrl).ToArray());
    }
}
