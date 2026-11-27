using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El título de una página (D-016-X3): el suyo manda; sin él, el de la marca; sin ninguno, su nombre con el del sitio,
/// salvo en la raíz, que se titula con el nombre que muestra el sitio.
/// </summary>
public sealed class TituloDeLaPaginaTests
{
    [Theory]
    [InlineData("Ropa y abrigo — Tienda", null, "Ropa", "Tienda", false, "Ropa y abrigo — Tienda")]
    [InlineData(null, "Tienda SynergosLabs", "Carrito", "Tienda", false, "Tienda SynergosLabs")]
    [InlineData(null, null, "Carrito", "Tienda", false, "Carrito — Tienda")]
    [InlineData("  ", null, "Mi cuenta", "Tienda", false, "Mi cuenta — Tienda")]
    [InlineData(null, null, "Tienda", "Tienda", true, "Tienda")]
    [InlineData(null, null, "Educacion", "Educación", true, "Educación")]
    [InlineData(null, null, "Tienda", "tienda", false, "Tienda")]
    [InlineData(null, null, null, "Tienda", false, "Tienda")]
    public void El_titulo_dice_de_que_sitio_es(string? propio, string? marca, string? nombre, string sitio, bool raiz, string esperado)
    {
        Assert.Equal(esperado, TituloDeLaPagina.Resolver(propio, marca, nombre, sitio, raiz));
    }
}
