using System.Globalization;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.Listados;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El listado de Propiedades (<c>synergos-data-grid</c>, fuente «inmuebles»): un arriendo dice «/mes» junto al precio.
/// Sin él, «$ 3.800.000» se leía como el precio de venta del apartamento (016, fichas y categorías).
/// </summary>
public sealed class InmueblesDelCatalogoTests
{
    private static PropertyListing Inmueble(string id, string operacion, decimal precio) =>
        new(id, id, $"Inmueble {id}", operacion, "apartamento", precio, "COP", "Medellín", "El Poblado",
            2, 2, 80, 4, 6.2, -75.5, string.Empty);

    private static IReadOnlyList<FilaDelListado> Filas(params PropertyListing[] inmuebles)
    {
        var catalogo = Substitute.For<IPropertyCatalogProvider>();
        catalogo.SearchAsync(Arg.Any<PropertyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PropertySearchResult(inmuebles, []));
        var peticion = new PeticionDelListado(null, CultureInfo.GetCultureInfo("es-CO"), TimeZoneInfo.Utc,
            (clave, respaldo) => respaldo);
        return new InmueblesDelCatalogo(catalogo).Filas(peticion);
    }

    private static string Precio(FilaDelListado fila) => fila.Specs!.Single(s => s.Label == "Precio").Value;

    [Fact]
    public void Un_arriendo_dice_por_mes_y_una_venta_no()
    {
        var filas = Filas(Inmueble("arriendo", "arriendo", 3_800_000m), Inmueble("venta", "venta", 720_000_000m));

        Assert.EndsWith("/mes", Precio(filas.Single(f => f.Id == "arriendo")), StringComparison.Ordinal);
        Assert.DoesNotContain("/mes", Precio(filas.Single(f => f.Id == "venta")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("arriendo", true)]
    [InlineData("rent", true)]
    [InlineData("venta", false)]
    [InlineData("sale", false)]
    [InlineData(null, false)]
    public void El_arriendo_se_reconoce_como_lo_dice_el_catalogo_o_la_api(string? operacion, bool esArriendo)
    {
        Assert.Equal(esArriendo, InmueblesDelCatalogo.EsArriendo(operacion));
    }

    [Fact]
    public void El_por_mes_sale_del_diccionario()
    {
        var catalogo = Substitute.For<IPropertyCatalogProvider>();
        catalogo.SearchAsync(Arg.Any<PropertyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PropertySearchResult([Inmueble("arriendo", "arriendo", 3_800_000m)], []));
        var peticion = new PeticionDelListado(null, CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc,
            (clave, respaldo) => clave == "DataGrid.PerMonth" ? "/mo" : clave == "DataGrid.Price" ? "Price" : respaldo);

        var fila = new InmueblesDelCatalogo(catalogo).Filas(peticion).Single();

        Assert.EndsWith("/mo", fila.Specs!.Single(s => s.Label == "Price").Value, StringComparison.Ordinal);
    }
}
