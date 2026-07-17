using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubProductCatalogProvider"/> (seam
/// <see cref="IProductCatalogProvider"/>, catálogo del marketplace — dominio
/// Tienda): los 4 casos canónicos (ADR 0075) — empty / happy / filter /
/// idempotent — más el detalle del producto (PDP: variantes/reviews/Q&amp;A).
/// </summary>
public class StubProductCatalogProviderTests
{
    private static IProductCatalogProvider Make() => new StubProductCatalogProvider();

    [Fact] // empty: una categoría inexistente no matchea ningún producto
    public async Task Search_UnknownCategory_ReturnsEmpty()
    {
        var result = await Make().SearchAsync(new ProductQuery(Category: "NoExiste"));

        Assert.Empty(result.Products);
        Assert.Equal(0, result.Total);

        // La faceta de categoría SIGUE llegando aunque no haya productos, y es deliberado: el
        // drill-down la cuenta EXCLUYÉNDOSE a sí misma, así que trae las categorías reales y
        // es la salida del callejón. Antes devolvía [] y el usuario que filtraba a cero se
        // quedaba sin filtros.
        var category = result.Facets.Single(f => f.Field == "category");
        Assert.NotEmpty(category.Values);
    }

    [Fact] // Una columna con título y ningún chip no es información, es ruido.
    public async Task Search_UnaFacetaSinValores_NoSeEmite()
    {
        // minRating es un umbral con tramos DECLARADOS. Si ningún producto los alcanza, sus
        // chips se caen y la faceta queda sin nada que ofrecer → no se emite.
        // Pasa de verdad: al mudar la Tienda a contenido, el rating es UGC derivado y vale 0
        // en todo, así que "Calificación" salía como una columna muerta en la PLP.
        var result = await Make().SearchAsync(WithFacet("minRating", "4.5"));

        // El seed de demo SÍ tiene ratings, así que aquí minRating sigue viva…
        Assert.Contains(result.Facets, f => f.Field == "minRating");
        // …y ninguna faceta emitida llega vacía.
        Assert.All(result.Facets, f => Assert.NotEmpty(f.Values));
    }

    [Fact] // happy: query vacía devuelve todo el catálogo + facetas derivadas
    public async Task Search_NoFilter_ReturnsAllWithFacets()
    {
        var result = await Make().SearchAsync(new ProductQuery());

        Assert.NotEmpty(result.Products);
        Assert.Equal(result.Products.Count, result.Total);
        // Facetas canónicas presentes (categoría + marca como mínimo).
        Assert.Contains(result.Facets, f => f.Field == "category");
        Assert.Contains(result.Facets, f => f.Field == "brand");
        // Cada producto trae precio + moneda + rating.
        Assert.All(result.Products, p =>
        {
            Assert.True(p.Price > 0m);
            Assert.False(string.IsNullOrWhiteSpace(p.Currency));
        });
    }

    [Fact] // filter: por categoría — solo devuelve productos de esa categoría
    public async Task Search_ByCategory_FiltersToCategory()
    {
        var result = await Make().SearchAsync(new ProductQuery(Category: "Tecnología"));

        Assert.NotEmpty(result.Products);
        Assert.All(result.Products, p => Assert.Equal("Tecnología", p.Category));
    }

    [Fact] // La faceta seleccionada NO se filtra a sí misma (drill-down).
    public async Task Search_ByCategory_LaFacetaDeCategoriaSigueMostrandoLasHermanas()
    {
        var result = await Make().SearchAsync(new ProductQuery(Category: "Tecnología"));

        var categoryFacet = result.Facets.Single(f => f.Field == "category");

        // Cambió con el motor transversal, y a mejor: antes la faceta se filtraba a sí misma
        // y reportaba SOLO "Tecnología", así que el usuario no podía cambiar de categoría sin
        // limpiar el filtro — veía un único chip, el que ya tenía puesto. Ahora ve las
        // hermanas con su conteo real, que es como se comporta cualquier PLP de verdad.
        Assert.True(categoryFacet.Values.Count > 1);
        Assert.Contains(categoryFacet.Values, v => v.Value == "Tecnología");
        Assert.Contains(categoryFacet.Values, v => v.Value == "Hogar");

        // Pero las OTRAS facetas sí se acotan a la categoría elegida.
        var brands = result.Facets.Single(f => f.Field == "brand").Values.Sum(v => v.Count);
        Assert.Equal(result.Total, brands);
    }

    [Fact] // filter: por texto — matchea nombre/marca case-insensitive
    public async Task Search_ByText_MatchesNameOrBrand()
    {
        var result = await Make().SearchAsync(new ProductQuery(Text: "laptop"));

        Assert.NotEmpty(result.Products);
        Assert.All(result.Products, p =>
            Assert.Contains("laptop", p.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static ProductQuery WithFacet(string field, params string[] values)
        => new(Facets: new Dictionary<string, IReadOnlyList<string>> { [field] = values });

    [Fact] // filter: por faceta de marca — solo esa marca
    public async Task Search_ByBrandFacet_FiltersToBrand()
    {
        var result = await Make().SearchAsync(WithFacet("brand", "Aurora"));

        Assert.NotEmpty(result.Products);
        Assert.All(result.Products, p => Assert.Equal("Aurora", p.Brand));
    }

    [Fact] // filter: minRating descarta los de rating bajo
    public async Task Search_ByMinRating_FiltersLowRated()
    {
        var result = await Make().SearchAsync(WithFacet("minRating", "4.5"));

        Assert.All(result.Products, p => Assert.True(p.Rating >= 4.5));
    }

    // ── El bug que cierra la fachada, sobre el catálogo REAL de la Tienda ──────────

    [Fact] // Verificado en vivo antes del fix: Aurora→1, Barista→1, "Aurora,Barista"→0.
    public async Task Search_DosMarcas_DevuelveLaUnion_NoCero()
    {
        var svc = Make();

        var aurora = await svc.SearchAsync(WithFacet("brand", "Aurora"));
        var barista = await svc.SearchAsync(WithFacet("brand", "Barista"));
        var ambas = await svc.SearchAsync(WithFacet("brand", "Aurora", "Barista"));

        Assert.NotEmpty(aurora.Products);
        Assert.NotEmpty(barista.Products);
        // Filtrar por dos marcas ya no devuelve MENOS que por una.
        Assert.Equal(aurora.Total + barista.Total, ambas.Total);
        Assert.All(ambas.Products, p => Assert.Contains(p.Brand, new[] { "Aurora", "Barista" }));
    }

    [Fact] // El chip de rating decía literalmente "4.0" porque el label no viajaba.
    public async Task Search_FacetaDeRating_TraeLabelLegibleYSuKind()
    {
        var result = await Make().SearchAsync(new ProductQuery());

        var rating = result.Facets.Single(f => f.Field == "minRating");
        Assert.Equal("Threshold", rating.Kind);
        Assert.Equal("4 estrellas o más", rating.Values.Single(v => v.Value == "4").Label);
    }

    [Fact] // El mismo bug de multi-valor, en el eje de categoría. Verificado en vivo: 2+2=0.
    public async Task Search_DosCategorias_DevuelveLaUnion_NoCero()
    {
        var svc = Make();

        var hogar = await svc.SearchAsync(WithFacet("category", "Hogar"));
        var deportes = await svc.SearchAsync(WithFacet("category", "Deportes"));
        var ambas = await svc.SearchAsync(WithFacet("category", "Hogar", "Deportes"));

        Assert.NotEmpty(hogar.Products);
        Assert.NotEmpty(deportes.Products);
        Assert.Equal(hogar.Total + deportes.Total, ambas.Total);
    }

    [Fact] // Total = los que pasan el filtro, NO los de la página: la UI cuenta páginas con esto.
    public async Task Search_Paginada_TotalEsElDelFiltro_NoElDeLaPagina()
    {
        var svc = Make();

        var todo = await svc.SearchAsync(new ProductQuery());
        var page1 = await svc.SearchAsync(new ProductQuery(Skip: 0, Take: 2));
        var page2 = await svc.SearchAsync(new ProductQuery(Skip: 2, Take: 2));

        Assert.Equal(2, page1.Products.Count);
        Assert.Equal(todo.Total, page1.Total);      // el total no encoge al paginar
        Assert.Equal(todo.Total, page2.Total);
        Assert.Empty(page1.Products.Select(p => p.Id).Intersect(page2.Products.Select(p => p.Id)));
    }

    [Fact] // El bug de tildes, a través de la fachada y con el seed real.
    public async Task Search_SinTildes_EncuentraLoAcentuado()
    {
        var conTilde = await Make().SearchAsync(new ProductQuery(Text: "Tecnología"));
        var sinTilde = await Make().SearchAsync(new ProductQuery(Text: "tecnologia"));

        Assert.NotEmpty(conTilde.Products);
        Assert.Equal(conTilde.Products.Select(p => p.Id), sinTilde.Products.Select(p => p.Id));
    }

    [Fact] // Las facetas cuentan sobre el universo filtrado, no sobre la página.
    public async Task Search_Paginada_LasFacetasNoEncogen()
    {
        var svc = Make();

        var todo = await svc.SearchAsync(new ProductQuery());
        var page = await svc.SearchAsync(new ProductQuery(Take: 1));

        var marcaTodo = todo.Facets.Single(f => f.Field == "brand").Values.Sum(v => v.Count);
        var marcaPage = page.Facets.Single(f => f.Field == "brand").Values.Sum(v => v.Count);

        Assert.Single(page.Products);
        Assert.Equal(marcaTodo, marcaPage);   // 1 producto en pantalla, los conteos intactos
    }

    [Fact] // sort: price-asc ordena por precio ascendente
    public async Task Search_SortPriceAsc_IsOrdered()
    {
        var result = await Make().SearchAsync(new ProductQuery(Sort: "price-asc"));

        var prices = result.Products.Select(p => p.Price).ToList();
        Assert.Equal(prices.OrderBy(x => x), prices);
    }

    [Fact] // idempotent: misma query → mismo resultado (catálogo estático/determinista)
    public async Task Search_SameQuery_IsDeterministic()
    {
        var svc = Make();
        var first = await svc.SearchAsync(new ProductQuery(Category: "Deportes", Sort: "price-desc"));
        var second = await svc.SearchAsync(new ProductQuery(Category: "Deportes", Sort: "price-desc"));

        Assert.Equal(
            first.Products.Select(p => p.Id),
            second.Products.Select(p => p.Id));
        Assert.Equal(first.Total, second.Total);
    }

    [Fact] // PDP happy: detalle resuelve producto + variantes + reviews + Q&A
    public async Task GetProduct_ReturnsDetailWithVariantsReviewsQuestions()
    {
        var detail = await Make().GetProductAsync("tec-laptop-pro-14");

        Assert.NotNull(detail);
        Assert.Equal("tec-laptop-pro-14", detail!.Product.Id);
        Assert.False(string.IsNullOrWhiteSpace(detail.Description));
        Assert.NotEmpty(detail.ImageUrls);
        Assert.NotEmpty(detail.Variants);
        Assert.All(detail.Variants, v => Assert.True(v.Price > 0m));
        Assert.NotEmpty(detail.Reviews);
        Assert.All(detail.Reviews, r => Assert.InRange(r.Rating, 1, 5));
        Assert.NotEmpty(detail.Questions);
        // Q&A: una pregunta contestada y una pendiente (Answer null) en el seed.
        Assert.Contains(detail.Questions, q => q.Answer is not null);
        Assert.Contains(detail.Questions, q => q.Answer is null);
    }

    [Fact] // PDP empty: producto inexistente → null
    public async Task GetProduct_Unknown_ReturnsNull()
    {
        Assert.Null(await Make().GetProductAsync("no-existe"));
    }

    [Fact] // stock: el resumen agrega el stock de las variantes
    public async Task Search_StockAggregatesVariants()
    {
        var detail = await Make().GetProductAsync("dep-tenis-running");
        Assert.NotNull(detail);
        var expected = detail!.Variants.Sum(v => v.Stock);
        Assert.Equal(expected, detail.Product.Stock);
    }
}
