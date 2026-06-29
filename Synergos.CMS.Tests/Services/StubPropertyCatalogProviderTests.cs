using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubPropertyCatalogProvider"/> (seam
/// <see cref="IPropertyCatalogProvider"/>, catálogo del portal inmobiliario): los
/// 4 casos canónicos (ADR 0075) — empty (query sin criterios) / happy (ficha por
/// id) / filter (filtros + facetas) / idempotent (mismo query, mismo resultado).
/// </summary>
public class StubPropertyCatalogProviderTests
{
    private static IPropertyCatalogProvider Make() => new StubPropertyCatalogProvider();

    [Fact] // empty: query sin criterios devuelve TODO el catálogo + facetas
    public async Task Search_NoCriteria_ReturnsAllWithFacets()
    {
        var result = await Make().SearchAsync(new PropertyQuery());

        Assert.NotEmpty(result.Listings);
        Assert.NotEmpty(result.Facets);
        // Destacados primero (orden estable de la grilla).
        Assert.True(result.Listings[0].Featured);
        // Hay faceta de tipo con conteos derivados del universo.
        Assert.Contains(result.Facets, f => f.Name == "type" && f.Values.Sum(v => v.Count) == result.Listings.Count);
    }

    [Fact] // happy: ficha por id trae specs + galería + ubicación geo + agente
    public async Task GetListing_ById_ReturnsDetail()
    {
        var detail = await Make().GetListingAsync("prop-001");

        Assert.NotNull(detail);
        Assert.Equal("prop-001", detail!.Summary.Id);
        Assert.NotEmpty(detail.Specs);
        Assert.NotEmpty(detail.Gallery);
        Assert.NotEqual(0d, detail.Location.Lat);
        Assert.False(string.IsNullOrWhiteSpace(detail.AgentName));
    }

    [Fact] // happy: ficha también resuelve por slug; id inexistente → null
    public async Task GetListing_BySlug_AndMissing()
    {
        var svc = Make();
        var bySlug = await svc.GetListingAsync("apto-chico-norte-bogota");
        Assert.NotNull(bySlug);
        Assert.Equal("prop-001", bySlug!.Summary.Id);

        Assert.Null(await svc.GetListingAsync("nope"));
    }

    [Fact] // filter: tipo + precio + ubicación se combinan en AND
    public async Task Search_Filters_CombineInAnd()
    {
        var result = await Make().SearchAsync(new PropertyQuery(
            Type: "apartamento", MaxPrice: 800_000_000m, Location: "Bogotá"));

        Assert.NotEmpty(result.Listings);
        Assert.All(result.Listings, l =>
        {
            Assert.Equal("apartamento", l.Type);
            Assert.True(l.Price <= 800_000_000m);
            Assert.Equal("Bogotá", l.City);
        });
    }

    [Fact] // filter: número mínimo de habitaciones filtra (Beds >= N)
    public async Task Search_BedsFilter_AppliesMinimum()
    {
        var result = await Make().SearchAsync(new PropertyQuery(Beds: 4));
        Assert.NotEmpty(result.Listings);
        Assert.All(result.Listings, l => Assert.True(l.Beds >= 4));
    }

    [Fact] // idempotent: el mismo query devuelve el mismo conjunto (determinista)
    public async Task Search_IsDeterministic()
    {
        var svc = Make();
        var q = new PropertyQuery(Type: "casa");
        var first = await svc.SearchAsync(q);
        var second = await svc.SearchAsync(q);

        Assert.Equal(
            first.Listings.Select(l => l.Id),
            second.Listings.Select(l => l.Id));
    }
}
