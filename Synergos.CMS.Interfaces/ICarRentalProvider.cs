namespace Synergos.CMS.Interfaces;

/// <summary>
/// Consulta de disponibilidad de alquiler de autos: lugar de retiro + lugar de
/// devolución (mismo o distinto) + fechas/horas de retiro y devolución. Los días
/// de alquiler los calcula el motor/proveedor a partir del rango; el DTO solo
/// transporta la intención. Si <see cref="DropoffLocation"/> es null/vacío se
/// asume devolución en el mismo lugar de retiro (one-way vs round-trip rental).
/// </summary>
public sealed record CarRentalQuery(
    string PickupLocation,
    string? DropoffLocation,
    DateOnly PickupDate,
    DateOnly DropoffDate);

/// <summary>
/// Una opción de auto reservable = categoría (código SIPP/ACRISS) + proveedor
/// (rentadora) con su precio total para los días consultados (es-CO vía
/// IPriceFormatter al renderizar), transmisión y si tiene aire acondicionado.
/// Es la unidad que la pantalla de Results lista y desde la que el cliente hace
/// Select. <see cref="PricePerDay"/> × <see cref="Days"/> = <see cref="TotalPrice"/>.
/// </summary>
public sealed record CarOffer(
    string CategoryCode,
    string CategoryName,
    string SippCode,
    string Provider,
    string Transmission,
    bool AirConditioning,
    decimal PricePerDay,
    int Days,
    decimal TotalPrice,
    string Currency);

/// <summary>
/// Búsqueda de disponibilidad del vertical Autos. Es la pieza del MOTOR que
/// resuelve "qué autos hay" para un lugar + fechas: <see cref="SearchAsync"/> →
/// lista de <see cref="CarOffer"/> (categoría/SIPP × proveedor + precio×días +
/// transmisión + aire). Tercer producto del carrito de viaje multi-producto,
/// paralelo a <see cref="IRoomAvailabilityProvider"/> (hoteles) y
/// <see cref="IFlightAvailabilityProvider"/> (vuelos).
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IFlightAvailabilityProvider"/> y
/// <see cref="IPaymentProvider"/>): el default <c>StubCarRentalProvider</c>
/// (Application, lógica pura/determinista) sirve un catálogo sembrado en memoria
/// (categorías × rentadoras) para que la demo corra end-to-end; el adapter real
/// (agregador de rentadoras con tarifas en vivo) se enchufa después sin tocar el
/// motor. ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface ICarRentalProvider
{
    /// <summary>
    /// Devuelve las opciones de auto disponibles para el lugar + fechas de
    /// <paramref name="query"/>. Lanza <see cref="ArgumentException"/> si la
    /// consulta es inválida (DropoffDate ≤ PickupDate, o PickupLocation vacío).
    /// </summary>
    Task<IReadOnlyList<CarOffer>> SearchAsync(CarRentalQuery query, CancellationToken cancellationToken = default);
}
