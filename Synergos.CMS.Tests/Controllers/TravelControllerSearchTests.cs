using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La búsqueda de vuelos y autos por la ruta directa (<c>GET /api/travel/search/flight|car</c>), con la forma que lee
/// travel-shell (lote 12, 006 T036; ADR 0083: la UI es la fuente de verdad).
/// </summary>
/// <remarks>
/// Medido en vivo en <c>/viajes/</c> de la copia piloto: el CMS contestaba los vuelos y el portal decía «Sin vuelos»,
/// porque su normalizador descarta toda oferta sin <c>title</c>; y los autos caían a los de ejemplo, porque el portal
/// pregunta con su <c>CarCriteria</c> (<c>location</c>, <c>pickUp</c>, <c>dropOff</c>) y el borde exigía
/// <c>pickupLocation</c>: un 400. Lo que ya se emitía y los nombres de antes siguen igual.
/// </remarks>
public sealed class TravelControllerSearchTests
{
    private static readonly DateOnly Ida = new(2026, 11, 20);
    private static readonly DateOnly Vuelta = new(2026, 11, 23);

    private readonly IFlightAvailabilityProvider _flights = Substitute.For<IFlightAvailabilityProvider>();
    private readonly ICarRentalProvider _cars = Substitute.For<ICarRentalProvider>();

    private TravelController BuildSut() => new(
        Substitute.For<IRoomAvailabilityProvider>(), _flights, _cars, Substitute.For<ITravelCartService>(),
        Substitute.For<IStayContentProvider>(), Substitute.For<IPriceFormatter>(), Substitute.For<IMemberAccessGate>());

    private static JsonElement Ofertas(IActionResult resultado)
    {
        var ok = Assert.IsType<OkObjectResult>(resultado);
        // Como lo serializa ASP.NET: camelCase.
        return JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("offers");
    }

    private static FlightOption Vuelo(string id, int escalas, int minutos, params FlightFare[] tarifas) => new(
        id,
        [new FlightSegment(id, "BOG", "CTG", new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 20, 13, 35, 0, TimeSpan.Zero), minutos)],
        tarifas, escalas, minutos);

    private static FlightFare Tarifa(string codigo, string familia, bool flexible = false) =>
        new(codigo, "economy", familia, flexible ? 918_000m : 680_000m, "COP", flexible, flexible, flexible);

    private static CarOffer Auto(string codigo, string transmision, bool aire) =>
        new(codigo, "Económico", "ECMR", "SynRent", transmision, aire, 95_000m, 3, 285_000m, "COP");

    [Fact] // happy: cada tarifa trae title y subtitle; lo de antes sigue
    public async Task Cada_vuelo_trae_title_y_subtitle()
    {
        _flights.SearchAsync(Arg.Any<FlightSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Vuelo("SYN2200", 0, 95, Tarifa("SYN2200-ECOBAS", "Economy Básica")),
            Vuelo("SYN2240", 1, 235, Tarifa("SYN2240-ECOBAS", "Economy Básica")),
            Vuelo("SYN2290", 2, 300, Tarifa("SYN2290-ECOBAS", "Economy Básica")),
        });

        var ofertas = Ofertas(await BuildSut().SearchFlight("BOG", "CTG", Ida, Vuelta));

        Assert.Equal("BOG → CTG", ofertas[0].GetProperty("title").GetString());
        Assert.Equal("SYN2200 · Directo · 1h 35m · Economy Básica", ofertas[0].GetProperty("subtitle").GetString());
        Assert.Equal("SYN2240 · 1 escala · 3h 55m · Economy Básica", ofertas[1].GetProperty("subtitle").GetString());
        Assert.Equal("SYN2290 · 2 escalas · 5h · Economy Básica", ofertas[2].GetProperty("subtitle").GetString());
        Assert.Equal("SYN2200-ECOBAS", ofertas[0].GetProperty("offerId").GetString());
        Assert.Equal("Economy Básica", ofertas[0].GetProperty("familyName").GetString());
    }

    [Fact] // happy: cada tarifa es su propia familia, con su código como id: el portal la elige y reserva con él
    public async Task Cada_vuelo_trae_su_familia_tarifaria()
    {
        _flights.SearchAsync(Arg.Any<FlightSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Vuelo("SYN2200", 0, 95, Tarifa("SYN2200-ECOBAS", "Economy Básica"), Tarifa("SYN2200-ECOFLEX", "Economy Flex", flexible: true)),
        });

        var ofertas = Ofertas(await BuildSut().SearchFlight("BOG", "CTG", Ida, Vuelta));

        var basica = Assert.Single(ofertas[0].GetProperty("fareFamilies").EnumerateArray());
        Assert.Equal("SYN2200-ECOBAS", basica.GetProperty("id").GetString());
        Assert.Equal("Economy Básica", basica.GetProperty("label").GetString());
        Assert.Equal(680_000m, basica.GetProperty("amount").GetDecimal());
        Assert.Equal("COP", basica.GetProperty("currency").GetString());
        Assert.Empty(basica.GetProperty("perks").EnumerateArray());
        var flex = Assert.Single(ofertas[1].GetProperty("fareFamilies").EnumerateArray());
        Assert.Equal("SYN2200-ECOFLEX", flex.GetProperty("id").GetString());
        Assert.Equal(918_000m, flex.GetProperty("amount").GetDecimal());
        Assert.Equal(
            new[] { "Equipaje incluido", "Cambios permitidos", "Reembolsable" },
            flex.GetProperty("perks").EnumerateArray().Select(p => p.GetString()!));
    }

    [Fact] // happy: con los nombres del portal contesta autos, con title, subtitle y sus facetas en español
    public async Task Los_autos_se_buscan_con_los_nombres_del_portal()
    {
        _cars.SearchAsync(Arg.Any<CarRentalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Auto("ECON", "manual", true), Auto("COMP", "automatic", false) });

        var ofertas = Ofertas(await BuildSut().SearchCar(location: " CTG ", pickUp: Ida, dropOff: Vuelta));

        await _cars.Received(1).SearchAsync(new CarRentalQuery("CTG", null, Ida, Vuelta), Arg.Any<CancellationToken>());
        Assert.Equal("Económico", ofertas[0].GetProperty("title").GetString());
        Assert.Equal("SynRent · Manual · A/C", ofertas[0].GetProperty("subtitle").GetString());
        Assert.Equal("SynRent · Automático", ofertas[1].GetProperty("subtitle").GetString());
        Assert.Equal("Económico", ofertas[0].GetProperty("carCategory").GetString());
        Assert.Equal("Manual", ofertas[0].GetProperty("carTransmission").GetString());
        Assert.Equal("Automático", ofertas[1].GetProperty("carTransmission").GetString());
        Assert.Equal("ECON", ofertas[0].GetProperty("offerId").GetString());
        Assert.Equal("manual", ofertas[0].GetProperty("transmission").GetString());
    }

    [Fact] // filter: los nombres de antes siguen valiendo
    public async Task Los_nombres_de_antes_siguen_valiendo()
    {
        _cars.SearchAsync(Arg.Any<CarRentalQuery>(), Arg.Any<CancellationToken>()).Returns(new[] { Auto("ECON", "manual", true) });

        Ofertas(await BuildSut().SearchCar(pickupLocation: "CTG", pickupDate: Ida, dropoffDate: Vuelta, dropoffLocation: "BAQ"));

        await _cars.Received(1).SearchAsync(new CarRentalQuery("CTG", "BAQ", Ida, Vuelta), Arg.Any<CancellationToken>());
    }

    [Fact] // empty: sin lugar o sin fechas no busca
    public async Task Sin_lugar_o_sin_fechas_no_busca()
    {
        var sut = BuildSut();

        Assert.IsType<BadRequestObjectResult>(await sut.SearchCar(pickUp: Ida, dropOff: Vuelta));
        Assert.IsType<BadRequestObjectResult>(await sut.SearchCar(location: "CTG", pickUp: Ida));
        Assert.IsType<BadRequestObjectResult>(await sut.SearchCar(location: "CTG", pickUp: Vuelta, dropOff: Ida));
        await _cars.DidNotReceive().SearchAsync(Arg.Any<CarRentalQuery>(), Arg.Any<CancellationToken>());
    }
}
