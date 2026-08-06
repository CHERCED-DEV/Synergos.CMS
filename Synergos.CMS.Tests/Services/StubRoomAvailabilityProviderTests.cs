using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubRoomAvailabilityProvider"/> (seam
/// <see cref="IRoomAvailabilityProvider"/>, búsqueda de disponibilidad del
/// vertical Hoteles): los 4 casos canónicos (ADR 0075) —
/// inválido / happy (search) / filter (ocupación) / idempotente (determinista).
/// </summary>
public class StubRoomAvailabilityProviderTests
{
    private static IRoomAvailabilityProvider Make() => new StubRoomAvailabilityProvider();

    private static AvailabilityQuery Query(int adults = 2, int[]? childAges = null, int nights = 2)
        => new(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1).AddDays(nights),
            new[] { new RoomOccupancy(adults, childAges ?? Array.Empty<int>()) });

    [Fact] // inválido: CheckOut <= CheckIn
    public async Task Search_CheckOutNotAfterCheckIn_Throws()
    {
        var query = new AvailabilityQuery(
            new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 5),
            new[] { new RoomOccupancy(2, Array.Empty<int>()) });

        await Assert.ThrowsAsync<ArgumentException>(() => Make().SearchAsync(query));
    }

    [Fact] // happy: devuelve ofertas refundable + non-refundable, precio = base × noches
    public async Task Search_TwoNights_YieldsRefundableAndNonRefundableOffers()
    {
        var offers = await Make().SearchAsync(Query(adults: 2, nights: 2));

        Assert.NotEmpty(offers);
        Assert.Contains(offers, o => o.Refundable);
        Assert.Contains(offers, o => !o.Refundable);
        Assert.All(offers, o => Assert.Equal("COP", o.Currency));

        // STD refundable: 220.000 × 2 noches = 440.000.
        var stdFlex = offers.Single(o => o.RatePlanCode == "STD-FLEX");
        Assert.Equal(440_000m, stdFlex.TotalPrice);
        // El non-refundable del mismo room type es más barato.
        var stdNref = offers.Single(o => o.RatePlanCode == "STD-NREF");
        Assert.True(stdNref.TotalPrice < stdFlex.TotalPrice);
    }

    [Fact] // filter: ocupación alta descarta los room types que no acomodan
    public async Task Search_HighOccupancy_FiltersOutSmallRooms()
    {
        // 4 adultos + 1 niño = 5 huéspedes → STD (max 2) y DLX (max 3) quedan fuera.
        var offers = await Make().SearchAsync(Query(adults: 4, childAges: new[] { 8 }));

        Assert.DoesNotContain(offers, o => o.RoomTypeCode == "STD");
        Assert.DoesNotContain(offers, o => o.RoomTypeCode == "DLX");
        // FAM (max 5) sí acomoda.
        Assert.Contains(offers, o => o.RoomTypeCode == "FAM");
    }

    [Fact] // idempotente/determinista: misma query → mismos resultados
    public async Task Search_IsDeterministic()
    {
        var provider = Make();
        var first = await provider.SearchAsync(Query());
        var second = await provider.SearchAsync(Query());

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Select(o => (o.RatePlanCode, o.TotalPrice)),
            second.Select(o => (o.RatePlanCode, o.TotalPrice)));
    }
}
