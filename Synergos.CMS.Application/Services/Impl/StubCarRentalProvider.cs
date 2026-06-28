using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICarRentalProvider"/> — disponibilidad STUB para que el
/// vertical Autos corra end-to-end en demo sin un agregador de rentadoras real
/// (mismo patrón stub-first que <c>StubFlightAvailabilityProvider</c>). Sirve un
/// catálogo sembrado en memoria (categorías SIPP × rentadoras) y calcula el
/// precio total como tarifa-por-día × días del alquiler.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). El adapter real (agregador de
/// rentadoras con tarifas en vivo) implementa la misma seam y se registra en su
/// lugar vía el composer sin tocar el motor. Tercer producto del carrito de
/// viaje multi-producto, paralelo a hoteles y vuelos.
/// </remarks>
public sealed class StubCarRentalProvider : ICarRentalProvider
{
    // Catálogo sembrado: 4 categorías SIPP/ACRISS, cada una de una rentadora,
    // con su tarifa base por día en COP. SIPP code: clase + tipo + transmisión +
    // combustible/AC (e.g. ECMR = Economy, 2/4-door, Manual, AC).
    private static readonly IReadOnlyList<CatalogCar> Catalog = new[]
    {
        new CatalogCar("ECON", "Económico", "ECMR", "SynRent", Transmission: "manual", AirConditioning: true, PricePerDay: 95_000m),
        new CatalogCar("COMP", "Compacto", "CDAR", "Localiza", Transmission: "automatic", AirConditioning: true, PricePerDay: 130_000m),
        new CatalogCar("SUV", "SUV", "IFAR", "Hertz", Transmission: "automatic", AirConditioning: true, PricePerDay: 210_000m),
        new CatalogCar("LUX", "Premium", "PDAR", "Avis", Transmission: "automatic", AirConditioning: true, PricePerDay: 340_000m),
    };

    public Task<IReadOnlyList<CarOffer>> SearchAsync(CarRentalQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.PickupLocation))
        {
            throw new ArgumentException("El lugar de retiro es obligatorio.", nameof(query));
        }
        if (query.DropoffDate <= query.PickupDate)
        {
            throw new ArgumentException("La fecha de devolución debe ser posterior a la de retiro.", nameof(query));
        }

        // Días de alquiler = diferencia del rango (mínimo 1, ya garantizado por
        // la validación de arriba). Precio total = tarifa/día × días.
        var days = query.DropoffDate.DayNumber - query.PickupDate.DayNumber;

        var offers = Catalog
            .Select(car => new CarOffer(
                CategoryCode: car.CategoryCode,
                CategoryName: car.CategoryName,
                SippCode: car.SippCode,
                Provider: car.Provider,
                Transmission: car.Transmission,
                AirConditioning: car.AirConditioning,
                PricePerDay: car.PricePerDay,
                Days: days,
                TotalPrice: car.PricePerDay * days,
                Currency: "COP"))
            .ToList();

        return Task.FromResult<IReadOnlyList<CarOffer>>(offers);
    }

    private readonly record struct CatalogCar(
        string CategoryCode,
        string CategoryName,
        string SippCode,
        string Provider,
        string Transmission,
        bool AirConditioning,
        decimal PricePerDay);
}
