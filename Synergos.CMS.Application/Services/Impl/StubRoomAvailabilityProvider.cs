using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IRoomAvailabilityProvider"/> — disponibilidad STUB para
/// que el vertical Hoteles corra end-to-end en demo sin un PMS real (mismo
/// patrón stub-first que <c>StubPaymentProvider</c>). Sirve un catálogo
/// sembrado en memoria (room types × rate plans) y calcula el precio total
/// como tarifa-base × noches.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). El adapter real (PMS /
/// channel-manager con webhooks para lock real-time) implementa la misma seam
/// y se registra en su lugar vía el composer sin tocar el motor.
/// </remarks>
public sealed class StubRoomAvailabilityProvider : IRoomAvailabilityProvider
{
    // Catálogo sembrado: 4 room types × 2 rate plans (refundable / non-refundable).
    // El non-refundable es ~10% más barato (research Booking.com).
    private static readonly IReadOnlyList<CatalogRoom> Catalog = new[]
    {
        new CatalogRoom("STD", "Habitación Estándar", 220_000m, 2, 6),
        new CatalogRoom("DLX", "Habitación Deluxe", 320_000m, 3, 4),
        new CatalogRoom("STE", "Suite Junior", 480_000m, 4, 2),
        new CatalogRoom("FAM", "Habitación Familiar", 540_000m, 5, 3),
    };

    public Task<IReadOnlyList<RoomOffer>> SearchAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.CheckOut <= query.CheckIn)
        {
            throw new ArgumentException("La fecha de salida debe ser posterior a la de entrada.", nameof(query));
        }
        if (query.Rooms is null || query.Rooms.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos una habitación con ocupación.", nameof(query));
        }

        var nights = query.CheckOut.DayNumber - query.CheckIn.DayNumber;
        // Capacidad pedida = la habitación de mayor ocupación de la query
        // (un room type debe acomodar a cada habitación solicitada).
        var maxGuestsPerRoom = query.Rooms.Max(r => r.Adults + r.ChildAges.Count);

        var offers = new List<RoomOffer>();
        foreach (var room in Catalog)
        {
            // Filtro por ocupación: descarta los room types que no acomodan.
            if (room.MaxOccupancy < maxGuestsPerRoom)
            {
                continue;
            }

            var basePerNight = room.BasePricePerNight * query.Rooms.Count;

            // Rate plan refundable (BAR, tarifa base, cancelación flexible).
            offers.Add(new RoomOffer(
                room.Code,
                room.Name,
                RatePlanCode: $"{room.Code}-FLEX",
                BoardBasis: "BB",
                TotalPrice: basePerNight * nights,
                Currency: "COP",
                Refundable: true,
                MinStayNights: 1,
                RoomsLeft: room.RoomsLeft));

            // Rate plan non-refundable (~10% más barato, prepago total).
            offers.Add(new RoomOffer(
                room.Code,
                room.Name,
                RatePlanCode: $"{room.Code}-NREF",
                BoardBasis: "RO",
                TotalPrice: Math.Round(basePerNight * nights * 0.90m, 0, MidpointRounding.AwayFromZero),
                Currency: "COP",
                Refundable: false,
                MinStayNights: 2,
                RoomsLeft: room.RoomsLeft));
        }

        return Task.FromResult<IReadOnlyList<RoomOffer>>(offers);
    }

    private readonly record struct CatalogRoom(string Code, string Name, decimal BasePricePerNight, int MaxOccupancy, int RoomsLeft);
}
