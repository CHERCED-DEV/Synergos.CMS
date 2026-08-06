using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IFlightAvailabilityProvider"/> — disponibilidad STUB para
/// que el vertical Aerolíneas corra end-to-end en demo sin un GDS/NDC real
/// (mismo patrón stub-first que <c>StubRoomAvailabilityProvider</c>). Sirve un
/// catálogo sembrado en memoria (rutas × itinerarios × familias tarifarias) y
/// calcula el precio total como tarifa-base × pasajeros pagos × tramos.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). El adapter real (GDS / NDC con
/// cotización en vivo) implementa la misma seam y se registra en su lugar vía
/// el composer sin tocar el motor.
/// </remarks>
public sealed class StubFlightAvailabilityProvider : IFlightAvailabilityProvider
{
    // Multiplicadores de precio por pasajero pago: adulto = tarifa plena,
    // niño = 75% (research aerolíneas), infante = gratis (sin asiento).
    private const decimal ChildFactor = 0.75m;

    // Catálogo sembrado: 3 rutas, cada una con 2-3 itinerarios (directo / con
    // escala). Cada itinerario expande a 3 familias tarifarias (economy-basic
    // non-refundable / economy-flex refundable / business). Precio base es
    // por-adulto-por-tramo en COP.
    private static readonly IReadOnlyList<CatalogRoute> Catalog = new[]
    {
        new CatalogRoute("BOG", "MDE", new[]
        {
            new CatalogItinerary("SYN1010", 0, 65,  220_000m, DepartHour: 6,  Connect: null),
            new CatalogItinerary("SYN1014", 0, 70,  260_000m, DepartHour: 12, Connect: null),
            new CatalogItinerary("SYN1090", 1, 185, 180_000m, DepartHour: 9,  Connect: "PEI"),
        }),
        new CatalogRoute("BOG", "CTG", new[]
        {
            new CatalogItinerary("SYN2200", 0, 95,  340_000m, DepartHour: 7,  Connect: null),
            new CatalogItinerary("SYN2240", 1, 235, 270_000m, DepartHour: 14, Connect: "MDE"),
        }),
        new CatalogRoute("BOG", "MIA", new[]
        {
            new CatalogItinerary("SYN8800", 0, 215, 1_450_000m, DepartHour: 8,  Connect: null),
            new CatalogItinerary("SYN8840", 1, 410, 1_180_000m, DepartHour: 16, Connect: "PTY"),
        }),
    };

    public Task<IReadOnlyList<FlightOption>> SearchAsync(FlightSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ReturnDate is { } ret && ret < query.DepartDate)
        {
            throw new ArgumentException("La fecha de regreso no puede ser anterior a la de ida.", nameof(query));
        }
        if (query.Adults + query.Children + query.Infants <= 0)
        {
            throw new ArgumentException("Se requiere al menos un pasajero.", nameof(query));
        }
        if (query.Adults <= 0 && (query.Children > 0 || query.Infants > 0))
        {
            throw new ArgumentException("Los menores deben viajar con al menos un adulto.", nameof(query));
        }

        // Pasajeros pagos ponderados (infantes gratis) × tramos del viaje
        // (1 = solo-ida, 2 = ida y vuelta).
        var payingUnits = query.Adults + query.Children * ChildFactor;
        var legs = query.ReturnDate is null ? 1 : 2;
        var priceMultiplier = payingUnits * legs;

        var options = new List<FlightOption>();
        foreach (var route in Catalog)
        {
            // Filtro por ruta: descarta las rutas que no son la consultada.
            if (!route.Matches(query.Origin, query.Destination))
            {
                continue;
            }

            foreach (var itin in route.Itineraries)
            {
                var segments = itin.BuildSegments(route, query.DepartDate);
                var baseTotal = itin.BasePricePerAdultPerLeg * priceMultiplier;

                var fares = new List<FlightFare>
                {
                    // Economy básica: no reembolsable, sin equipaje, sin cambios.
                    new(
                        FareCode: $"{itin.FlightNumber}-ECOBAS",
                        Cabin: "economy",
                        FamilyName: "Economy Básica",
                        TotalPrice: baseTotal,
                        Currency: "COP",
                        Refundable: false,
                        BaggageIncluded: false,
                        ChangesAllowed: false),
                    // Economy flex: reembolsable, equipaje incluido, cambios.
                    new(
                        FareCode: $"{itin.FlightNumber}-ECOFLEX",
                        Cabin: "economy",
                        FamilyName: "Economy Flex",
                        TotalPrice: Math.Round(baseTotal * 1.35m, 0, MidpointRounding.AwayFromZero),
                        Currency: "COP",
                        Refundable: true,
                        BaggageIncluded: true,
                        ChangesAllowed: true),
                    // Business: reembolsable, equipaje incluido, cambios.
                    new(
                        FareCode: $"{itin.FlightNumber}-BUS",
                        Cabin: "business",
                        FamilyName: "Business",
                        TotalPrice: Math.Round(baseTotal * 2.60m, 0, MidpointRounding.AwayFromZero),
                        Currency: "COP",
                        Refundable: true,
                        BaggageIncluded: true,
                        ChangesAllowed: true),
                };

                options.Add(new FlightOption(
                    FlightId: itin.FlightNumber,
                    Segments: segments,
                    Fares: fares,
                    Stops: itin.Stops,
                    TotalDurationMinutes: itin.TotalDurationMinutes));
            }
        }

        return Task.FromResult<IReadOnlyList<FlightOption>>(options);
    }

    private readonly record struct CatalogRoute(string Origin, string Destination, IReadOnlyList<CatalogItinerary> Itineraries)
    {
        public bool Matches(string origin, string destination)
            => string.Equals(Origin, origin?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Destination, destination?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct CatalogItinerary(
        string FlightNumber,
        int Stops,
        int TotalDurationMinutes,
        decimal BasePricePerAdultPerLeg,
        int DepartHour,
        string? Connect)
    {
        // Construye los segmentos del itinerario sobre la fecha de ida. Los
        // horarios son deterministas (derivados de DepartHour + duraciones), en
        // UTC. Un directo = 1 segmento; una escala = 2 segmentos partiendo la
        // duración total con una espera fija de 45 min en conexión.
        public IReadOnlyList<FlightSegment> BuildSegments(CatalogRoute route, DateOnly departDate)
        {
            var depart = new DateTimeOffset(
                departDate.Year, departDate.Month, departDate.Day,
                DepartHour, 0, 0, TimeSpan.Zero);

            if (Stops == 0 || Connect is null)
            {
                var arrive = depart.AddMinutes(TotalDurationMinutes);
                return new[]
                {
                    new FlightSegment(FlightNumber, route.Origin, route.Destination, depart, arrive, TotalDurationMinutes),
                };
            }

            const int layoverMinutes = 45;
            var flyingMinutes = TotalDurationMinutes - layoverMinutes;
            var firstLegMinutes = flyingMinutes / 2;
            var secondLegMinutes = flyingMinutes - firstLegMinutes;

            var leg1Arrive = depart.AddMinutes(firstLegMinutes);
            var leg2Depart = leg1Arrive.AddMinutes(layoverMinutes);
            var leg2Arrive = leg2Depart.AddMinutes(secondLegMinutes);

            return new[]
            {
                new FlightSegment(FlightNumber, route.Origin, Connect, depart, leg1Arrive, firstLegMinutes),
                new FlightSegment($"{FlightNumber}B", Connect, route.Destination, leg2Depart, leg2Arrive, secondLegMinutes),
            };
        }
    }
}
