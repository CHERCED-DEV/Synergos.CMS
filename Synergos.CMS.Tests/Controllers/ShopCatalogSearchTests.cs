using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La ruta HTTP de la búsqueda de Tienda: <c>?category=Hogar,Deportes</c> → ProductQuery.
/// </summary>
/// <remarks>
/// <b>Por qué existe este fichero:</b> los dos bugs que la revisión adversarial encontró en
/// T5 Ola 0 vivían AQUÍ, entre el query string y la seam — el CSV de <c>category</c> que no
/// se partía (2+2=0) y el desbordamiento de <c>(page-1)*pageSize</c>. Los 17 tests del
/// provider pasaban verdes porque le entregaban la lista ya partida: probaban la seam, no la
/// frontera. Un bug de traducción del wire solo lo atrapa un test del wire.
/// </remarks>
public class ShopCatalogSearchTests
{
    /// <summary>Captura el <see cref="ProductQuery"/> que el controller construye.</summary>
    private static (ShopCatalogController Controller, List<ProductQuery> Captured) Make()
    {
        var captured = new List<ProductQuery>();
        var catalog = Substitute.For<IProductCatalogProvider>();
        catalog
            .SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured.Add(call.Arg<ProductQuery>());
                return Task.FromResult(new ProductSearchResult(
                    Array.Empty<CatalogProductSummary>(),
                    Array.Empty<ProductFacet>(),
                    0));
            });

        var priceFormatter = Substitute.For<IPriceFormatter>();
        priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string>()).Returns("$ 0");

        var controller = new ShopCatalogController(
            catalog,
            Substitute.For<IShopOrderService>(),
            priceFormatter,
            Substitute.For<IUserCollection>(),
            Substitute.For<IOrderTrackingService>(),
            Substitute.For<IReturnService>(),
            Substitute.For<IMessagingService>(),
            Substitute.For<IMemberAccessGate>());

        return (controller, captured);
    }

    private static async Task<ProductQuery> QueryFor(
        string? q = null, string? category = null, string? brand = null,
        string? minRating = null, string? sort = null, int? page = null, int? take = null)
    {
        var (controller, captured) = Make();
        await controller.Search(q, category, brand, minRating, sort, page, take, CancellationToken.None);
        return Assert.Single(captured);
    }

    // ── El bug ALTA: el CSV de category no se partía ───────────────────────────────

    [Fact] // Verificado en vivo: ?category=Hogar → 2, ?category=Deportes → 2, juntas → 0.
    public async Task Category_ConCsv_SeParteEnDosValores()
    {
        var query = await QueryFor(category: "Hogar,Deportes");

        var values = Assert.Contains("category", query.Facets!);
        Assert.Equal(new[] { "Hogar", "Deportes" }, values);
    }

    [Fact] // La UI manda TODAS las facetas como CSV (values.join(',')): las tres se parten.
    public async Task Brand_YMinRating_TambienSeParten()
    {
        var query = await QueryFor(brand: "Aurora,Barista", minRating: "4.5,4.0");

        Assert.Equal(new[] { "Aurora", "Barista" }, Assert.Contains("brand", query.Facets!));
        Assert.Equal(new[] { "4.5", "4.0" }, Assert.Contains("minRating", query.Facets!));
    }

    [Fact] // Un solo valor sigue siendo un solo valor (no se rompe el caso normal).
    public async Task UnSoloValor_LlegaComoUnaListaDeUno()
        => Assert.Equal(new[] { "Hogar" }, Assert.Contains("category", (await QueryFor(category: "Hogar")).Facets!));

    [Fact] // Los espacios alrededor de las comas los pone cualquier cliente.
    public async Task Csv_ConEspacios_SeRecorta()
        => Assert.Equal(new[] { "Hogar", "Deportes" },
            Assert.Contains("category", (await QueryFor(category: " Hogar , Deportes ")).Facets!));

    [Fact] // Comas de sobra no deben producir facetas vacías que filtren a cero.
    public async Task Csv_ConComasDeSobra_NoProduceValoresVacios()
        => Assert.Equal(new[] { "Hogar" },
            Assert.Contains("category", (await QueryFor(category: "Hogar,,")).Facets!));

    [Fact]
    public async Task SinFiltros_NoMandaFacetas()
        => Assert.Null((await QueryFor()).Facets);

    // ── Paginación: page 1-based → Skip 0-based ───────────────────────────────────

    [Theory]
    [InlineData(null, 0)]   // sin page → la primera
    [InlineData(1, 0)]
    [InlineData(2, 12)]
    [InlineData(3, 24)]
    [InlineData(0, 0)]      // page<1 es un enlace roto, no un 400
    [InlineData(-5, 0)]
    public async Task Page_SeTraduceASkip(int? page, int expectedSkip)
        => Assert.Equal(expectedSkip, (await QueryFor(page: page)).Skip);

    [Fact] // (page-1)*pageSize DESBORDA int y sale negativo; el motor trata Skip<0 como 0,
           // así que sin la guarda ?page=1431655766 devolvía la PÁGINA 1 como si nada.
    public async Task Page_Enorme_NoDesborda_NiDevuelveLaPrimeraPagina()
    {
        var query = await QueryFor(page: int.MaxValue, take: 3);

        Assert.True(query.Skip > 0, $"Skip salió {query.Skip}: desbordó y el usuario vería la página 1.");
    }

    [Fact]
    public async Task Take_SeHonra_YSinTakeUsaElDefault()
    {
        Assert.Equal(5, (await QueryFor(take: 5)).Take);
        Assert.Equal(12, (await QueryFor()).Take);
    }
}
