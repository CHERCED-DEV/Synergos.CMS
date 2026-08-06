namespace Synergos.CMS.Interfaces;

/// <summary>
/// Ocupación solicitada para UNA habitación: el selector del vertical
/// Hoteles es por-habitación (no por-reserva), cada una con adultos +
/// niños con edad. Las bandas de edad (infante 0-2 · niño 2-12 ·
/// adulto 12+) y las reglas (niños siempre con ≥1 adulto, max occupancy)
/// las aplica el motor/hotel; el DTO solo transporta la intención.
/// </summary>
public sealed record RoomOccupancy(int Adults, IReadOnlyList<int> ChildAges);

/// <summary>
/// Consulta de disponibilidad: rango de fechas + ocupación por habitación.
/// El número de habitaciones es <c>Rooms.Count</c>.
/// </summary>
public sealed record AvailabilityQuery(
    DateOnly CheckIn,
    DateOnly CheckOut,
    IReadOnlyList<RoomOccupancy> Rooms);

/// <summary>
/// Una oferta reservable = Room Type × Rate Plan, con su board basis,
/// precio total para las noches consultadas (es-CO vía IPriceFormatter al
/// renderizar), si es reembolsable y el cupo restante. Es la unidad que
/// la pantalla de Results lista y desde la que el huésped hace Select.
/// </summary>
public sealed record RoomOffer(
    string RoomTypeCode,
    string RoomTypeName,
    string RatePlanCode,
    string BoardBasis,
    decimal TotalPrice,
    string Currency,
    bool Refundable,
    int? MinStayNights,
    int RoomsLeft);

/// <summary>
/// Búsqueda de disponibilidad del vertical Hoteles. Es la pieza del MOTOR
/// que resuelve "qué hay disponible" para un rango de fechas + ocupación:
/// <see cref="SearchAsync"/> → lista de <see cref="RoomOffer"/> (Room Type
/// × Rate Plan + precio + restricciones).
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IBundleRegistryClient"/> y
/// <see cref="IPaymentProvider"/>): el default <c>StubRoomAvailabilityProvider</c>
/// (Application, lógica pura) sirve un catálogo sembrado en memoria para que
/// la demo corra end-to-end; el adapter real (PMS / channel-manager con
/// webhooks para lock real-time) se enchufa después sin tocar el motor.
/// ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface IRoomAvailabilityProvider
{
    /// <summary>
    /// Devuelve las ofertas disponibles para el rango + ocupación de
    /// <paramref name="query"/>. Lanza <see cref="ArgumentException"/> si
    /// el rango es inválido (CheckOut ≤ CheckIn).
    /// </summary>
    Task<IReadOnlyList<RoomOffer>> SearchAsync(AvailabilityQuery query, CancellationToken cancellationToken = default);
}
