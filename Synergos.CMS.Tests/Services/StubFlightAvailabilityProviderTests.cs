using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubFlightAvailabilityProvider"/> (seam
/// <see cref="IFlightAvailabilityProvider"/>, búsqueda de disponibilidad del
/// vertical Aerolíneas): los 4 casos canónicos (ADR 0075) —
/// inválido / happy (search) / filter (por ruta) / idempotente (determinista).
/// </summary>
public class StubFlightAvailabilityProviderTests
{
    private static IFlightAvailabilityProvider Make() => new StubFlightAvailabilityProvider();

    private static FlightSearchQuery Query(
        string origin = "BOG",
        string destination = "MDE",
        bool roundTrip = false,
        int adults = 1,
        int children = 0,
        int infants = 0)
        => new(
            origin,
            destination,
            new DateOnly(2026, 7, 1),
            roundTrip ? new DateOnly(2026, 7, 8) : null,
            adults,
            children,
            infants);

    [Fact] // inválido: ReturnDate anterior a DepartDate
    public async Task Search_ReturnBeforeDepart_Throws()
    {
        var query = new FlightSearchQuery(
            "BOG", "MDE",
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 5),
            Adults: 1, Children: 0, Infants: 0);

        await Assert.ThrowsAsync<ArgumentException>(() => Make().SearchAsync(query));
    }

    [Fact] // happy: devuelve opciones con fares refundable + non-refundable, infante gratis
    public async Task Search_OneAdultOneWay_YieldsFaresAcrossCabins()
    {
        var offers = await Make().SearchAsync(Query(adults: 1));

        Assert.NotEmpty(offers);
        var allFares = offers.SelectMany(o => o.Fares).ToList();
        Assert.Contains(allFares, f => f.Refundable);
        Assert.Contains(allFares, f => !f.Refundable);
        Assert.All(allFares, f => Assert.Equal("COP", f.Currency));

        // SYN1010 (BOG-MDE directo) economy básica: 220.000 × 1 adulto × 1 tramo.
        var basic = offers
            .Single(o => o.FlightId == "SYN1010")
            .Fares.Single(f => f.FareCode == "SYN1010-ECOBAS");
        Assert.Equal(220_000m, basic.TotalPrice);
        Assert.False(basic.Refundable);

        // Infante gratis: agregarlo no cambia el precio.
        var withInfant = await Make().SearchAsync(Query(adults: 1, infants: 1));
        var basicInfant = withInfant
            .Single(o => o.FlightId == "SYN1010")
            .Fares.Single(f => f.FareCode == "SYN1010-ECOBAS");
        Assert.Equal(basic.TotalPrice, basicInfant.TotalPrice);
    }

    [Fact] // happy/ida-y-vuelta: el precio dobla por los 2 tramos
    public async Task Search_RoundTrip_DoublesBasePrice()
    {
        var oneWay = await Make().SearchAsync(Query(adults: 1, roundTrip: false));
        var roundTrip = await Make().SearchAsync(Query(adults: 1, roundTrip: true));

        var owBasic = oneWay.Single(o => o.FlightId == "SYN1010")
            .Fares.Single(f => f.FareCode == "SYN1010-ECOBAS");
        var rtBasic = roundTrip.Single(o => o.FlightId == "SYN1010")
            .Fares.Single(f => f.FareCode == "SYN1010-ECOBAS");

        Assert.Equal(owBasic.TotalPrice * 2, rtBasic.TotalPrice);
    }

    [Fact] // filter: la búsqueda solo devuelve opciones de la ruta consultada
    public async Task Search_FiltersByRoute()
    {
        var offers = await Make().SearchAsync(Query(origin: "BOG", destination: "CTG"));

        Assert.NotEmpty(offers);
        // Todas las opciones empiezan/terminan en la ruta pedida.
        Assert.All(offers, o =>
        {
            Assert.Equal("BOG", o.Segments[0].Origin);
            Assert.Equal("CTG", o.Segments[^1].Destination);
        });
        // No filtra otras rutas: BOG-MDE (SYN1010) no aparece.
        Assert.DoesNotContain(offers, o => o.FlightId == "SYN1010");

        // Ruta inexistente → vacío (no lanza).
        var none = await Make().SearchAsync(Query(origin: "BOG", destination: "XXX"));
        Assert.Empty(none);
    }

    [Fact] // idempotente/determinista: misma query → mismos resultados
    public async Task Search_IsDeterministic()
    {
        var provider = Make();
        var first = await provider.SearchAsync(Query());
        var second = await provider.SearchAsync(Query());

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.SelectMany(o => o.Fares).Select(f => (f.FareCode, f.TotalPrice)),
            second.SelectMany(o => o.Fares).Select(f => (f.FareCode, f.TotalPrice)));
    }
}
