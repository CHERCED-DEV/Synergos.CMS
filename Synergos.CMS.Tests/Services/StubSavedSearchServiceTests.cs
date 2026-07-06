using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubSavedSearchService"/> (seam <see cref="ISavedSearchService"/>,
/// búsquedas guardadas + alertas del portal inmobiliario, doc §4): los 4 casos
/// canónicos (ADR 0075) — empty (usuario sin búsquedas) / happy (guardar + re-leer +
/// matches) / filter (criterios se re-ejecutan contra el catálogo) / idempotent
/// (misma búsqueda no duplica). Verifica la COMPOSICIÓN sobre IUserCollection +
/// IPropertyCatalogProvider (DIP, sin store paralelo).
/// </summary>
public class StubSavedSearchServiceTests
{
    private const string Owner = "camila@synergos.co";
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static ISavedSearchService Make() => new StubSavedSearchService(
        new StubUserCollection(() => Clock),
        new StubPropertyCatalogProvider(),
        () => Clock);

    [Fact] // empty: usuario sin búsquedas → lista vacía (sin lanzar)
    public async Task GetForOwner_Unknown_ReturnsEmpty()
    {
        var svc = Make();
        Assert.Empty(await svc.GetForOwnerAsync(Owner));
        Assert.Empty(await svc.GetForOwnerAsync(""));
    }

    [Fact] // happy: guardar una búsqueda la deja re-leíble con su id + criterios
    public async Task Save_Happy_RoundTrips()
    {
        var svc = Make();
        var criteria = new PropertyQuery(Type: "apartamento", Location: "Bogotá", Beds: 2);

        var saved = await svc.SaveAsync(Owner, criteria, "Aptos en Bogotá");

        Assert.StartsWith("search_", saved.Id);
        Assert.Equal("Aptos en Bogotá", saved.Label);

        var all = await svc.GetForOwnerAsync(Owner);
        var back = Assert.Single(all);
        Assert.Equal(saved.Id, back.Id);
        Assert.Equal("apartamento", back.Criteria.Type);
        Assert.Equal("Bogotá", back.Criteria.Location);
        Assert.Equal(2, back.Criteria.Beds);
    }

    [Fact] // filter: matches re-ejecuta los criterios contra el catálogo (alerta)
    public async Task GetMatches_ReExecutesCriteria()
    {
        var svc = Make();
        var saved = await svc.SaveAsync(Owner, new PropertyQuery(Type: "apartamento"), "Aptos");

        var matches = await svc.GetMatchesAsync(saved.Id);

        Assert.Equal(saved.Id, matches.SearchId);
        Assert.True(matches.Count > 0);
        Assert.Equal(matches.Count, matches.Listings.Count);
        Assert.All(matches.Listings, l => Assert.Equal("apartamento", l.Type));
    }

    [Fact] // idempotent: guardar la MISMA búsqueda dos veces no la duplica (mismo id)
    public async Task Save_SameCriteria_IsIdempotent()
    {
        var svc = Make();
        var criteria = new PropertyQuery(Type: "casa", Location: "Medellín");

        var first = await svc.SaveAsync(Owner, criteria);
        var second = await svc.SaveAsync(Owner, criteria);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await svc.GetForOwnerAsync(Owner));
    }

    [Fact] // inválido: matches de una búsqueda inexistente lanza
    public async Task GetMatches_Unknown_Throws()
    {
        var svc = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.GetMatchesAsync("search_nope"));
    }

    [Fact] // inválido: guardar sin owner lanza
    public async Task Save_NoOwner_Throws()
    {
        var svc = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SaveAsync("", new PropertyQuery()));
    }
}
