using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IEventCatalogProvider"/> — catálogo de eventos STUB para que
/// el vertical Eventos corra end-to-end en demo sin un índice/ticketing real (mismo
/// patrón stub-first que <c>StubRoomAvailabilityProvider</c> /
/// <c>StubFlightAvailabilityProvider</c> / <c>StubProductCatalogProvider</c>).
/// Sirve un catálogo sembrado en memoria (varios eventos × tiers × seat-map),
/// aplica el filtro de texto libre y resuelve la ficha completa de un evento.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero dependencia
/// de Umbraco/AspNetCore (ADR 0002). El seat-map de los eventos modo <c>reserved</c>
/// es JSON-compatible con el componente <c>synergos-seat-map</c> (zonas → filas →
/// asientos). El adapter real (Examine sobre <c>eventPage</c>, o un ticketing
/// externo) implementa la misma seam y se registra en su lugar vía el composer sin
/// tocar el motor ni el módulo Angular.
/// </remarks>
public sealed class StubEventCatalogProvider : IEventCatalogProvider
{
    private const string Currency = "COP";

    // Catálogo sembrado: 4 eventos en categorías distintas, mezclando modo
    // general (cantidad por tier) y reserved (seat-map por zona).
    private static readonly IReadOnlyList<EventDetail> Catalog = BuildCatalog();

    public Task<IReadOnlyList<EventSummary>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        IEnumerable<EventDetail> source = Catalog;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            source = source.Where(e =>
                e.Summary.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Summary.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Summary.City.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Summary.Venue.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var results = source
            .Select(e => e.Summary)
            .OrderBy(s => s.StartUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<EventSummary>>(results);
    }

    public Task<EventDetail?> GetEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Task.FromResult<EventDetail?>(null);
        }

        var id = eventId.Trim();
        var match = Catalog.FirstOrDefault(e =>
            string.Equals(e.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Summary.Slug, id, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    private static IReadOnlyList<EventDetail> BuildCatalog()
    {
        var festival = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-festival-estereo",
                Slug: "festival-estereo-2026",
                Title: "Festival Estéreo 2026",
                Category: "Música",
                City: "Bogotá",
                Venue: "Parque Simón Bolívar",
                StartUtc: new DateTimeOffset(2026, 8, 15, 21, 0, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/festival-estereo.webp",
                PriceFrom: 180_000m,
                Currency: Currency,
                Mode: "general"),
            Description: "Dos escenarios, más de 20 artistas nacionales e internacionales y food trucks. La cita musical del año en Bogotá.",
            Organizer: "Estéreo Producciones",
            Tiers: new[]
            {
                new EventTier("GEN", "General", 180_000m, Currency, Capacity: 5000, Remaining: 1240, MaxPerOrder: 6),
                new EventTier("VIP", "VIP", 420_000m, Currency, Capacity: 800, Remaining: 95, MaxPerOrder: 4),
                new EventTier("EARLY", "Early Bird", 140_000m, Currency, Capacity: 1000, Remaining: 0, MaxPerOrder: 6),
            },
            SeatMap: null);

        var teatro = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-concierto-sinfonico",
                Slug: "concierto-sinfonico-octubre",
                Title: "Concierto Sinfónico de Octubre",
                Category: "Música",
                City: "Medellín",
                Venue: "Teatro Metropolitano",
                StartUtc: new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/sinfonico.webp",
                PriceFrom: 90_000m,
                Currency: Currency,
                Mode: "reserved"),
            Description: "La Orquesta Filarmónica interpreta un programa de clásicos. Asientos numerados por zona.",
            Organizer: "Teatro Metropolitano",
            Tiers: new[]
            {
                new EventTier("PLATEA", "Platea", 180_000m, Currency, Capacity: 12, Remaining: 9, MaxPerOrder: 6, ZoneId: "platea"),
                new EventTier("BALCON", "Balcón", 90_000m, Currency, Capacity: 12, Remaining: 12, MaxPerOrder: 6, ZoneId: "balcon"),
            },
            SeatMap: new EventSeatMap(
                VenueName: "Teatro Metropolitano",
                Zones: new[]
                {
                    new EventZone(
                        Id: "platea",
                        Name: "Platea",
                        Price: 180_000m,
                        Currency: Currency,
                        TierCode: "PLATEA",
                        Rows: new[]
                        {
                            BuildRow("A", 6, soldLabels: new[] { "A3" }),
                            BuildRow("B", 6, soldLabels: new[] { "B1", "B2" }),
                        }),
                    new EventZone(
                        Id: "balcon",
                        Name: "Balcón",
                        Price: 90_000m,
                        Currency: Currency,
                        TierCode: "BALCON",
                        Rows: new[]
                        {
                            BuildRow("C", 6, soldLabels: Array.Empty<string>()),
                            BuildRow("D", 6, soldLabels: Array.Empty<string>()),
                        }),
                }));

        var conferencia = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-cumbre-tech",
                Slug: "cumbre-tech-2026",
                Title: "Cumbre Tech 2026",
                Category: "Conferencia",
                City: "Bogotá",
                Venue: "Ágora Centro de Convenciones",
                StartUtc: new DateTimeOffset(2026, 9, 22, 13, 0, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/cumbre-tech.webp",
                PriceFrom: 250_000m,
                Currency: Currency,
                Mode: "general"),
            Description: "Keynotes de líderes de la industria, talleres prácticos y networking. El evento de tecnología más grande del país.",
            Organizer: "Synergos Labs",
            Tiers: new[]
            {
                new EventTier("STD", "Estándar", 250_000m, Currency, Capacity: 1200, Remaining: 430, MaxPerOrder: 10),
                new EventTier("PRO", "Pro (talleres incluidos)", 480_000m, Currency, Capacity: 300, Remaining: 58, MaxPerOrder: 5),
            },
            SeatMap: null);

        var teatroInfantil = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-obra-infantil",
                Slug: "obra-infantil-navidad",
                Title: "Obra Infantil: Cuento de Navidad",
                Category: "Teatro",
                City: "Cali",
                Venue: "Teatro Municipal",
                StartUtc: new DateTimeOffset(2026, 12, 14, 19, 0, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/obra-infantil.webp",
                PriceFrom: 45_000m,
                Currency: Currency,
                Mode: "general"),
            Description: "Una adaptación familiar del clásico de Dickens, con música en vivo. Apta para todo público.",
            Organizer: "Teatro Municipal de Cali",
            Tiers: new[]
            {
                new EventTier("GEN", "General", 45_000m, Currency, Capacity: 400, Remaining: 210, MaxPerOrder: 8),
            },
            SeatMap: null);

        return new[] { festival, teatro, conferencia, teatroInfantil };
    }

    // Construye una fila de N asientos; los que estén en soldLabels arrancan como
    // "sold" (el resto "free"). Determinista para que la demo sea reproducible.
    private static EventRow BuildRow(string rowLabel, int count, IReadOnlyList<string> soldLabels)
    {
        var seats = new List<EventSeat>(count);
        for (var i = 1; i <= count; i++)
        {
            var label = $"{rowLabel}{i}";
            var status = soldLabels.Contains(label, StringComparer.OrdinalIgnoreCase) ? "sold" : "free";
            seats.Add(new EventSeat($"seat-{label}", label, status));
        }
        return new EventRow(rowLabel, seats);
    }
}
