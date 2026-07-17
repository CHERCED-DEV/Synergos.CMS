namespace Synergos.CMS.Interfaces;

/// <summary>
/// Consulta de disponibilidad de vuelos: ruta (origen → destino) + fechas
/// (ida, y opcionalmente vuelta para ida-y-vuelta) + pasajeros por banda de
/// edad. Las bandas (infante 0-2 sin asiento · niño 2-12 · adulto 12+) y las
/// reglas tarifarias las aplica el motor/aerolínea; el DTO solo transporta la
/// intención. Si <see cref="ReturnDate"/> es null la búsqueda es solo-ida.
/// </summary>
public sealed record FlightSearchQuery(
    string Origin,
    string Destination,
    DateOnly DepartDate,
    DateOnly? ReturnDate,
    int Adults,
    int Children,
    int Infants);

/// <summary>
/// Un tramo de vuelo (un despegue → un aterrizaje) operado por un número de
/// vuelo. Una <see cref="FlightOption"/> con escala tiene 2+ segmentos; un
/// directo tiene 1. Horarios en UTC (<see cref="DateTimeOffset"/>); el render
/// los localiza a la zona del aeropuerto.
/// </summary>
public sealed record FlightSegment(
    string FlightNumber,
    string Origin,
    string Destination,
    DateTimeOffset DepartUtc,
    DateTimeOffset ArriveUtc,
    int DurationMinutes);

/// <summary>
/// Una familia tarifaria reservable dentro de una <see cref="FlightOption"/>
/// (Cabin × Family): su código, cabina (economy/business), nombre comercial de
/// la familia, precio total para los pasajeros de la consulta (es-CO vía
/// IPriceFormatter al renderizar) y las reglas (reembolsable, equipaje
/// incluido, cambios permitidos). Es la unidad desde la que el pasajero hace
/// Select.
/// </summary>
public sealed record FlightFare(
    string FareCode,
    string Cabin,
    string FamilyName,
    decimal TotalPrice,
    string Currency,
    bool Refundable,
    bool BaggageIncluded,
    bool ChangesAllowed);

/// <summary>
/// Una opción de vuelo reservable = itinerario (uno o más
/// <see cref="FlightSegment"/>) + sus familias tarifarias
/// (<see cref="FlightFare"/>), con el número de escalas y la duración total.
/// Es la unidad que la pantalla de Results lista para la ruta + fechas
/// consultadas.
/// </summary>
public sealed record FlightOption(
    string FlightId,
    IReadOnlyList<FlightSegment> Segments,
    IReadOnlyList<FlightFare> Fares,
    int Stops,
    int TotalDurationMinutes);

/// <summary>
/// Búsqueda de disponibilidad del vertical Aerolíneas. Es la pieza del MOTOR
/// que resuelve "qué vuelos hay" para una ruta + fechas + pasajeros:
/// <see cref="SearchAsync"/> → lista de <see cref="FlightOption"/> (itinerario
/// + familias tarifarias + precio + reglas).
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IRoomAvailabilityProvider"/> y
/// <see cref="IPaymentProvider"/>): el default <c>StubFlightAvailabilityProvider</c>
/// (Application, lógica pura) sirve un catálogo sembrado en memoria para que la
/// demo corra end-to-end; el adapter real (GDS / NDC con cotización en vivo) se
/// enchufa después sin tocar el motor. ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface IFlightAvailabilityProvider
{
    /// <summary>
    /// Devuelve las opciones de vuelo disponibles para la ruta + fechas +
    /// pasajeros de <paramref name="query"/>. Lanza
    /// <see cref="ArgumentException"/> si la consulta es inválida (sin
    /// pasajeros, o ReturnDate anterior a DepartDate).
    /// </summary>
    Task<IReadOnlyList<FlightOption>> SearchAsync(FlightSearchQuery query, CancellationToken cancellationToken = default);
}
