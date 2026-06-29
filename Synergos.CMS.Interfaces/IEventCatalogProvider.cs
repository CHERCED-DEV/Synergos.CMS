namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resumen de un evento para el catálogo/agenda (cara de asistente). Es la
/// unidad que la pantalla de catálogo lista para la búsqueda; el detalle
/// (tiers + seat-map) se resuelve aparte con <see cref="IEventCatalogProvider.GetEventAsync"/>.
/// </summary>
public sealed record EventSummary(
    string Id,
    string Slug,
    string Title,
    string Category,
    string City,
    string Venue,
    DateTimeOffset StartUtc,
    string ImageUrl,
    decimal PriceFrom,
    string Currency,
    string Mode);

/// <summary>
/// Una categoría de ticket reservable de un evento (General / VIP / Early-bird).
/// Es la unidad de selección en modo <c>general</c> (cantidad por tier) y la
/// referencia de precio por zona en modo <c>reserved</c>. <see cref="Remaining"/>
/// es el aforo restante del tier (alimenta el "quedan N").
/// </summary>
public sealed record EventTier(
    string Code,
    string Name,
    decimal Price,
    string Currency,
    int Capacity,
    int Remaining,
    int MaxPerOrder,
    string? ZoneId = null);

/// <summary>
/// Un asiento individual del mapa de zona. <see cref="Status"/>: free | hold |
/// sold. El precio efectivo lo da la <see cref="EventZone.Price"/> de su zona.
/// </summary>
public sealed record EventSeat(
    string Id,
    string Label,
    string Status);

/// <summary>Una fila de asientos dentro de una zona (alimenta el componente seat-map).</summary>
public sealed record EventRow(
    string Label,
    IReadOnlyList<EventSeat> Seats);

/// <summary>
/// Una zona del venue (Platea / Palco / General) con su precio y sus filas de
/// asientos. El tier asociado (<see cref="TierCode"/>) liga la zona con el aforo
/// del <see cref="EventTier"/>.
/// </summary>
public sealed record EventZone(
    string Id,
    string Name,
    decimal Price,
    string Currency,
    string TierCode,
    IReadOnlyList<EventRow> Rows);

/// <summary>
/// Mapa de asientos del venue para el componente <c>synergos-seat-map</c>: zonas
/// → filas → asientos. Solo presente en eventos modo <c>reserved</c>; en modo
/// <c>general</c> es null (la selección es por cantidad de tier).
/// </summary>
public sealed record EventSeatMap(
    string VenueName,
    IReadOnlyList<EventZone> Zones);

/// <summary>
/// Ficha completa de un evento: el resumen + descripción/organizador + sus tiers
/// (precio/aforo) + el seat-map (si es modo reserved). Es lo que la pantalla de
/// ficha renderiza y desde donde el asistente hace Select.
/// </summary>
public sealed record EventDetail(
    EventSummary Summary,
    string Description,
    string Organizer,
    IReadOnlyList<EventTier> Tiers,
    EventSeatMap? SeatMap);

/// <summary>
/// Catálogo de eventos del vertical Eventos. Es la pieza del MOTOR que resuelve
/// "qué eventos hay" + "el detalle de este evento": <see cref="SearchAsync"/> →
/// lista de <see cref="EventSummary"/> (filtrada por texto); <see cref="GetEventAsync"/>
/// → <see cref="EventDetail"/> (tiers + seat-map) o null si no existe.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IRoomAvailabilityProvider"/> /
/// <see cref="IFlightAvailabilityProvider"/>): el default
/// <c>StubEventCatalogProvider</c> (Application, lógica pura) sirve un catálogo
/// sembrado en memoria (varios eventos × tiers × seat-map) para que la demo corra
/// end-to-end; el adapter real (Examine sobre eventPage, o un ticketing externo)
/// se enchufa después sin tocar el motor. ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface IEventCatalogProvider
{
    /// <summary>
    /// Devuelve los eventos del catálogo que matchean el texto libre
    /// <paramref name="query"/> (título / categoría / ciudad / venue). Si es
    /// null/vacío, devuelve todos, ordenados por fecha ascendente.
    /// </summary>
    Task<IReadOnlyList<EventSummary>> SearchAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la ficha del evento por id o slug, o null si no existe.
    /// </summary>
    Task<EventDetail?> GetEventAsync(string eventId, CancellationToken cancellationToken = default);
}
