using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La «Demo en vivo» de las fichas de app del hub (D-016-Q1): un sitio sin productos que nombra una categoría la busca
/// en el sitio que la tiene; un sitio con productos, o un bloque sin categoría, no sale de su sitio.
/// </summary>
public sealed class CatalogoEnElHubTests
{
    [Theory]
    [InlineData(false, "Ropa", true)]
    [InlineData(false, "Apartamentos", true)]
    [InlineData(false, null, false)]
    [InlineData(false, "  ", false)]
    [InlineData(true, "Ropa", false)]
    public void Sale_del_sitio_solo_sin_productos_y_con_categoria(bool tieneProductos, string? categoria, bool sale)
    {
        Assert.Equal(sale, DefaultShopQuery.BuscarFueraDelSitio(tieneProductos, categoria));
    }
}
