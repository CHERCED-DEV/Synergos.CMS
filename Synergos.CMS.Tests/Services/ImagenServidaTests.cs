using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Una foto de la biblioteca se pide al ancho en que se ve, en WebP (D-016-X2); lo que no es una foto de /media/ queda igual.
/// </summary>
public sealed class ImagenServidaTests
{
    [Theory]
    [InlineData("/media/abc/foto.jpg", 640, "/media/abc/foto.jpg?width=640&format=webp&quality=80")]
    [InlineData("/media/abc/ilustración-booking.PNG", 800, "/media/abc/ilustración-booking.PNG?width=800&format=webp&quality=80")]
    [InlineData("/media/abc/foto.jpg?width=1200&height=630&rmode=crop", 640, "/media/abc/foto.jpg?width=1200&height=630&rmode=crop")]
    [InlineData("/media/abc/logo.svg", 640, "/media/abc/logo.svg")]
    [InlineData("https://otro.sitio/media/foto.jpg", 640, "https://otro.sitio/media/foto.jpg")]
    [InlineData("/img/favicon.png", 640, "/img/favicon.png")]
    [InlineData("", 640, "")]
    [InlineData(null, 640, null)]
    public void Se_pide_al_ancho_en_que_se_ve(string? url, int ancho, string? esperada)
    {
        Assert.Equal(esperada, ImagenServida.Url(url, ancho));
    }

    [Theory]
    [InlineData("http://localhost:6300/media/abc/synergos-proposito.png", 1200, "http://localhost:6300/media/abc/synergos-proposito.png?width=1200&format=webp&quality=80")]
    [InlineData("https://synergoslabs.co/media/abc/foto.jpg", 1600, "https://synergoslabs.co/media/abc/foto.jpg?width=1600&format=webp&quality=80")]
    [InlineData("/media/abc/foto.jpg", 256, "/media/abc/foto.jpg?width=256&format=webp&quality=80")]
    [InlineData("http://localhost:6300/media/abc/foto.jpg?width=900", 1600, "http://localhost:6300/media/abc/foto.jpg?width=900")]
    [InlineData("http://localhost:6300/media/abc/logo.svg", 1600, "http://localhost:6300/media/abc/logo.svg")]
    [InlineData("http://localhost:6300/media/abc/clip.mp4", 1600, "http://localhost:6300/media/abc/clip.mp4")]
    [InlineData(null, 1600, null)]
    public void Un_medio_resuelto_por_Umbraco_se_pide_al_ancho_aunque_venga_absoluto(string? url, int ancho, string? esperada)
    {
        Assert.Equal(esperada, ImagenServida.DelMedio(url, ancho));
    }
}
