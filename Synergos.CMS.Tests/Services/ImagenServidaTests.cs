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
}
