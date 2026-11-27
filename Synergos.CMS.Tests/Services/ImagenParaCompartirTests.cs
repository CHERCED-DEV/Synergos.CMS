using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La imagen para compartir (D-016-X1): se pide a ImageSharp en 1200 × 630 recortada, conserve o no la URL su consulta,
/// y la imagen propia de un producto, un equipo o un inmueble va antes que la de los bloques.
/// </summary>
public sealed class ImagenParaCompartirTests
{
    [Theory]
    [InlineData("https://sitio/media/a/foto.jpg", "https://sitio/media/a/foto.jpg?width=1200&height=630&rmode=crop")]
    [InlineData("https://sitio/media/a/foto.jpg?v=2", "https://sitio/media/a/foto.jpg?v=2&width=1200&height=630&rmode=crop")]
    public void Se_pide_en_el_tamano_de_las_redes(string url, string esperada)
    {
        Assert.Equal(esperada, ImagenParaCompartir.ParaCompartir(url));
    }

    [Fact]
    public void La_imagen_propia_va_primero_y_la_del_producto_antes_que_ninguna()
    {
        Assert.Equal("productImages", ImagenParaCompartir.PropiedadesPropias[0]);
        Assert.Contains("equipmentCover", ImagenParaCompartir.PropiedadesPropias);
    }
}
