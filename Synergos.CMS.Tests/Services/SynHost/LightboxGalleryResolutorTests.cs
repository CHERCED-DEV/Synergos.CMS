using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="LightboxGalleryResolutor"/>: el TEXTO <c>imagesJson</c> (<c>fullUrl</c>/
/// <c>thumbUrl</c>/<c>alt</c>/<c>caption</c>) llega como la LISTA <c>images</c> (<c>src</c>/<c>thumb</c>/
/// <c>alt</c>/<c>caption</c>) —la vista mandaba el texto y la galería salía vacía (D1)—.
/// </summary>
public sealed class LightboxGalleryResolutorTests
{
    private const string Imagenes =
        """[{"thumbUrl":"/media/sala-t.jpg","fullUrl":"/media/sala.jpg","alt":"Sala","caption":"La sala"},{"fullUrl":"/media/cocina.jpg"}]""";

    private readonly ILogger<LightboxGalleryResolutor> _log = Substitute.For<ILogger<LightboxGalleryResolutor>>();

    private LightboxGalleryResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_imagenes_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("imagesJson", Imagenes),
            ("columns", "4"))).Props);

        Assert.Equal(new[] { "images", "columns" }, cable.Keys);
        var images = (JsonElement)cable["images"]!;
        Assert.Equal(2, images.GetArrayLength());
        Assert.Equal("/media/sala.jpg", images[0].GetProperty("src").GetString());
        Assert.Equal("/media/sala-t.jpg", images[0].GetProperty("thumb").GetString());
        Assert.Equal("La sala", images[0].GetProperty("caption").GetString());
        Assert.False(images[0].TryGetProperty("fullUrl", out _));
        Assert.False(images[1].TryGetProperty("thumb", out _));
        Assert.Equal(4, ((JsonElement)cable["columns"]!).GetInt32());
    }

    [Fact]
    public void Una_imagen_sin_fullUrl_no_viaja_y_se_anota()
    {
        var images = Resolutor().Resolver(ElementoFalso.Con(
            ("imagesJson", """[{"thumbUrl":"/media/solo-miniatura.jpg"},{"fullUrl":"/media/a.jpg"}]"""))).Props.Images;

        Assert.Equal(new[] { new LightboxGalleryImage("/media/a.jpg") }, images);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("imagesJson", Imagenes));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Images, Resolutor().Resolver(elemento).Props.Images);
    }
}
