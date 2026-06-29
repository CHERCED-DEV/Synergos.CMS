using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubEventCatalogProvider"/> (seam <see cref="IEventCatalogProvider"/>,
/// catálogo de eventos del vertical Eventos): los 4 casos canónicos (ADR 0075) —
/// empty (sin query → todo) / happy (ficha completa) / filter (texto) / idempotent
/// (lecturas estables) — más el seat-map de los eventos reserved.
/// </summary>
public class StubEventCatalogProviderTests
{
    private static IEventCatalogProvider Make() => new StubEventCatalogProvider();

    [Fact] // empty: sin query devuelve todo el catálogo, ordenado por fecha
    public async Task Search_NoQuery_ReturnsAllOrderedByDate()
    {
        var results = await Make().SearchAsync(null);

        Assert.NotEmpty(results);
        var dates = results.Select(r => r.StartUtc).ToList();
        Assert.Equal(dates.OrderBy(d => d), dates);
    }

    [Fact] // happy: ficha por id trae tiers; un evento reserved trae seat-map
    public async Task GetEvent_Reserved_HasTiersAndSeatMap()
    {
        var detail = await Make().GetEventAsync("evt-concierto-sinfonico");

        Assert.NotNull(detail);
        Assert.Equal("reserved", detail!.Summary.Mode);
        Assert.NotEmpty(detail.Tiers);
        Assert.NotNull(detail.SeatMap);
        Assert.NotEmpty(detail.SeatMap!.Zones);
        // El seat-map liga zona ↔ tier (compatible con synergos-seat-map).
        Assert.All(detail.SeatMap.Zones, z => Assert.NotEmpty(z.Rows));
    }

    [Fact] // happy: un evento general no trae seat-map (selección por cantidad)
    public async Task GetEvent_General_HasNoSeatMap()
    {
        var detail = await Make().GetEventAsync("festival-estereo-2026"); // por slug

        Assert.NotNull(detail);
        Assert.Equal("general", detail!.Summary.Mode);
        Assert.Null(detail.SeatMap);
    }

    [Fact] // filter: el texto matchea título/categoría/ciudad/venue
    public async Task Search_FiltersByText()
    {
        var byCity = await Make().SearchAsync("Medellín");
        Assert.All(byCity, e => Assert.Contains("Medellín", e.City));

        var byCategory = await Make().SearchAsync("Conferencia");
        Assert.All(byCategory, e => Assert.Equal("Conferencia", e.Category));

        // Sin match → vacío (no lanza).
        Assert.Empty(await Make().SearchAsync("zzz-no-existe"));
    }

    [Fact] // idempotent: lecturas repetidas devuelven el mismo resultado; id inexistente → null
    public async Task GetEvent_IsStableAndUnknownReturnsNull()
    {
        var svc = Make();
        var first = await svc.GetEventAsync("evt-cumbre-tech");
        var second = await svc.GetEventAsync("evt-cumbre-tech");

        Assert.NotNull(first);
        Assert.Equal(first!.Summary.Id, second!.Summary.Id);
        Assert.Equal(first.Tiers.Count, second.Tiers.Count);

        Assert.Null(await svc.GetEventAsync("nope"));
        Assert.Null(await svc.GetEventAsync(""));
    }
}
