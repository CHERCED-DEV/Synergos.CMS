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

    [Fact] // empty: una categoría inexistente no matchea nada (sin productos ni facetas)
    public async Task Search_UnknownCategory_ReturnsEmpty()
    {
        var result = await Make().SearchAsync(new ProductQuery(Category: "NoExiste"));

        Assert.Empty(result.Products);
        Assert.Empty(result.Facets);
        Assert.Equal(0, result.Total);
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
        // La faceta de categoría reporta solo la categoría filtrada.
        var categoryFacet = result.Facets.Single(f => f.Field == "category");
        Assert.Single(categoryFacet.Values);
        Assert.Equal("Tecnología", categoryFacet.Values[0].Value);
    }

    [Fact] // filter: por texto — matchea nombre/marca case-insensitive
    public async Task Search_ByText_MatchesNameOrBrand()
    {
        var result = await Make().SearchAsync(new ProductQuery(Text: "laptop"));

        Assert.NotEmpty(result.Products);
        Assert.All(result.Products, p =>
            Assert.Contains("laptop", p.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // filter: por faceta de marca — solo esa marca
    public async Task Search_ByBrandFacet_FiltersToBrand()
    {
        var facets = new Dictionary<string, string> { ["brand"] = "Aurora" };
        var result = await Make().SearchAsync(new ProductQuery(Facets: facets));

        Assert.NotEmpty(result.Products);
        Assert.All(result.Products, p => Assert.Equal("Aurora", p.Brand));
    }

    [Fact] // filter: minRating descarta los de rating bajo
    public async Task Search_ByMinRating_FiltersLowRated()
    {
        var facets = new Dictionary<string, string> { ["minRating"] = "4.5" };
        var result = await Make().SearchAsync(new ProductQuery(Facets: facets));

        Assert.All(result.Products, p => Assert.True(p.Rating >= 4.5));
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
