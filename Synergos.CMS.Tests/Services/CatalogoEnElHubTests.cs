using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La «Demo en vivo» de las fichas de app del hub (D-016-Q1): un sitio sin productos que nombra una categoría la busca
/// en el sitio que la tiene; un sitio con productos, o un bloque sin categoría, no sale de su sitio; y una categoría que
/// no existe deja la lista vacía.
/// </summary>
public sealed class CatalogoEnElHubTests
{
    private sealed record Producto(string Nombre, string? Sitio, string? Categoria);

    // La plataforma entera, como la ve el hub: la Tienda con Ropa y Tecnología, Propiedades con Apartamentos y un
    // servicio de Booking colgado de su sitio, sin categoría de producto.
    private static readonly Producto[] Plataforma =
    [
        new("Buzo con cierre", "Tienda", "Ropa"),
        new("Gorra clásica", "Tienda", "Ropa"),
        new("Hub USB-C", "Tienda", "Tecnología"),
        new("Apartamento en Chicó", "Propiedades", "Apartamentos"),
        new("Masaje 60 min", "Booking", null),
    ];

    private static IEnumerable<Producto> DeLaCategoria(IEnumerable<Producto> productos, string categoria) =>
        DefaultShopQuery.DeLaCategoria(productos, p => p.Categoria is null ? null : (p.Categoria, p.Categoria), categoria);

    [Fact]
    public void El_hub_sin_productos_encuentra_la_categoria_en_el_sitio_que_la_tiene()
    {
        Assert.True(DefaultShopQuery.BuscarFueraDelSitio(elSitioTieneProductos: false, "Ropa"));

        var ropa = DeLaCategoria(Plataforma, "ropa").Select(p => p.Nombre).ToList();

        Assert.Equal(["Buzo con cierre", "Gorra clásica"], ropa);
        Assert.Equal(["Apartamento en Chicó"], DeLaCategoria(Plataforma, "Apartamentos").Select(p => p.Nombre));
    }

    [Theory]
    [InlineData("Ropa")]
    [InlineData(null)]
    public void Un_sitio_con_productos_no_sale_de_su_sitio(string? categoria)
    {
        Assert.False(DefaultShopQuery.BuscarFueraDelSitio(elSitioTieneProductos: true, categoria));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Sin_categoria_el_hub_no_mezcla_catalogos(string? categoria)
    {
        Assert.False(DefaultShopQuery.BuscarFueraDelSitio(elSitioTieneProductos: false, categoria));
    }

    [Fact]
    public void Una_categoria_que_no_existe_deja_la_lista_vacia()
    {
        Assert.Empty(DeLaCategoria(Plataforma, "Penthouses"));
    }
}
