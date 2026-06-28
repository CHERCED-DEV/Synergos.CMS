using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCarRentalProvider"/> (seam <see cref="ICarRentalProvider"/>,
/// tercer producto del carrito de viaje): los 4 casos canónicos (ADR 0075) —
/// inválido / happy (search) / filter (precio por días) / idempotente (determinista).
/// </summary>
public class StubCarRentalProviderTests
{
    private static ICarRentalProvider Make() => new StubCarRentalProvider();

    private static CarRentalQuery Query(
        string pickup = "BOG",
        string? dropoff = null,
        int days = 3)
        => new(pickup, dropoff, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1).AddDays(days));

    [Fact] // inválido: DropoffDate ≤ PickupDate
    public async Task Search_DropoffNotAfterPickup_Throws()
    {
        var query = new CarRentalQuery("BOG", null, new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 5));
        await Assert.ThrowsAsync<ArgumentException>(() => Make().SearchAsync(query));
    }

    [Fact] // inválido: lugar de retiro vacío
    public async Task Search_EmptyPickup_Throws()
    {
        var query = new CarRentalQuery("  ", null, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 4));
        await Assert.ThrowsAsync<ArgumentException>(() => Make().SearchAsync(query));
    }

    [Fact] // happy: devuelve categorías con SIPP/proveedor/transmisión/aircon en COP
    public async Task Search_YieldsCategoriesAcrossProviders()
    {
        var offers = await Make().SearchAsync(Query());

        Assert.NotEmpty(offers);
        Assert.All(offers, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.SippCode));
            Assert.False(string.IsNullOrWhiteSpace(o.Provider));
            Assert.Equal("COP", o.Currency);
            Assert.Contains(o.Transmission, new[] { "manual", "automatic" });
        });
        // Hay variedad de categorías (económico … premium) de distintas rentadoras.
        Assert.True(offers.Select(o => o.Provider).Distinct().Count() > 1);
        Assert.Contains(offers, o => o.CategoryCode == "ECON");
        Assert.Contains(offers, o => o.AirConditioning);
    }

    [Fact] // filter/precio: TotalPrice = PricePerDay × días del rango
    public async Task Search_TotalScalesWithRentalDays()
    {
        var threeDays = await Make().SearchAsync(Query(days: 3));
        var sixDays = await Make().SearchAsync(Query(days: 6));

        var econ3 = threeDays.Single(o => o.CategoryCode == "ECON");
        var econ6 = sixDays.Single(o => o.CategoryCode == "ECON");

        Assert.Equal(3, econ3.Days);
        Assert.Equal(econ3.PricePerDay * 3, econ3.TotalPrice);
        Assert.Equal(6, econ6.Days);
        Assert.Equal(econ3.TotalPrice * 2, econ6.TotalPrice);
    }

    [Fact] // idempotente/determinista: misma query → mismos resultados
    public async Task Search_IsDeterministic()
    {
        var provider = Make();
        var first = await provider.SearchAsync(Query());
        var second = await provider.SearchAsync(Query());

        Assert.Equal(
            first.Select(o => (o.CategoryCode, o.TotalPrice)),
            second.Select(o => (o.CategoryCode, o.TotalPrice)));
    }
}
