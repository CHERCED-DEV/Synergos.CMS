using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IStayContentProvider"/> — fichas de estadía STUB para la
/// cara viajera del dominio Booking (OLA 2, doc 21 §2.2). Siembra 6 propiedades
/// realistas CO (Cartagena ×2 / Medellín / Bogotá / Eje Cafetero ×2) con geo
/// REAL, galería, amenities, specs y resumen de reviews, para que la ficha rica
/// y el mapa del search (SH-8) corran end-to-end sin un CMS/PMS real.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). Cuatro estadías "hospedan" los
/// room types del <c>StubRoomAvailabilityProvider</c> (STD/DLX/STE/FAM) — así el
/// search de hotel hereda las coordenadas de la propiedad por oferta; las otras
/// dos son direct-only (solo ficha). El adapter real (contenido CMS /
/// channel-manager) implementa la misma seam sin tocar el motor.
/// </remarks>
public sealed class StubStayContentProvider : IStayContentProvider
{
    /// <summary>Una estadía de muestra no tiene opiniones: nadie se ha alojado (016, D-016-C25).</summary>
    private static readonly StayReviewSummary SinOpiniones = new(0, 0, string.Empty, Array.Empty<StayReviewCategoryScore>());

    // Geo real: Getsemaní 10.4224,-75.5468 · Bocagrande 10.3997,-75.5567 ·
    // Provenza (El Poblado) 6.2088,-75.5673 · Zona T (Bogotá) 4.6669,-74.0536 ·
    // Salento 4.6372,-75.5709 · Filandia 4.6747,-75.6584.
    private static readonly IReadOnlyList<StayDetail> Catalog = new[]
    {
        new StayDetail(
            StayId: "ctg-getsemani",
            Name: "Casa Getsemaní Boutique",
            City: "Cartagena",
            Region: "Bolívar",
            Address: "Getsemaní, Cartagena",
            Description: "Casa republicana restaurada en el corazón de Getsemaní, a 5 minutos a pie "
                + "de la Ciudad Amurallada. Patios coloniales, piscina en el solar y terraza con "
                + "vista a los techos del centro histórico.",
            Gallery: GalleryFor("ctg-getsemani"),
            Amenities: new[] { "Wifi de alta velocidad", "Piscina exterior", "Desayuno según la tarifa", "Aire acondicionado", "Terraza con vista", "Traslado al aeropuerto" },
            Specs: new[]
            {
                new StaySpec("Habitaciones", "12"),
                new StaySpec("Huéspedes máx. por habitación", "2"),
                new StaySpec("Tamaño promedio", "28 m²"),
                new StaySpec("Check-in", "3:00 p. m."),
                new StaySpec("Check-out", "12:00 m."),
            },
            Geo: new StayGeo(10.4224, -75.5468),
            Reviews: SinOpiniones,
            RoomTypeCodes: new[] { "STD" }),

        new StayDetail(
            StayId: "ctg-bocagrande",
            Name: "Torre Bocagrande Beach Resort",
            City: "Cartagena",
            Region: "Bolívar",
            Address: "Bocagrande, Cartagena",
            Description: "Resort frente al mar en la primera línea de Bocagrande: playa privada, "
                + "tres piscinas, club de niños y gastronomía caribeña. Ideal para familias que "
                + "quieren sol y mar sin salir del hotel.",
            Gallery: GalleryFor("ctg-bocagrande"),
            Amenities: new[] { "Playa privada", "3 piscinas", "Club de niños", "Todo incluido opcional", "Gimnasio", "Wifi de alta velocidad", "Parqueadero" },
            Specs: new[]
            {
                new StaySpec("Habitaciones", "180"),
                new StaySpec("Huéspedes máx. por habitación", "5"),
                new StaySpec("Tamaño promedio", "42 m²"),
                new StaySpec("Check-in", "3:00 p. m."),
                new StaySpec("Check-out", "1:00 p. m."),
            },
            Geo: new StayGeo(10.3997, -75.5567),
            Reviews: SinOpiniones,
            RoomTypeCodes: new[] { "FAM" }),

        new StayDetail(
            StayId: "med-provenza",
            Name: "Lofts del Ciprés",
            City: "Medellín",
            Region: "Antioquia",
            Address: "Provenza, El Poblado, Medellín",
            Description: "Lofts de diseño en plena Provenza, la cuadra más vibrante de El Poblado. "
                + "Rooftop con jacuzzi, coworking y café de especialidad en el lobby — a pasos de "
                + "los mejores restaurantes de la ciudad.",
            Gallery: GalleryFor("med-provenza"),
            Amenities: new[] { "Rooftop con jacuzzi", "Coworking", "Café de especialidad", "Wifi de alta velocidad", "Aire acondicionado", "Pet friendly" },
            Specs: new[]
            {
                new StaySpec("Habitaciones", "36"),
                new StaySpec("Huéspedes máx. por habitación", "3"),
                new StaySpec("Tamaño promedio", "35 m²"),
                new StaySpec("Check-in", "3:00 p. m."),
                new StaySpec("Check-out", "12:00 m."),
            },
            Geo: new StayGeo(6.2088, -75.5673),
            Reviews: SinOpiniones,
            RoomTypeCodes: new[] { "DLX" }),

        new StayDetail(
            StayId: "bog-zonat",
            Name: "Zona T Executive Suites",
            City: "Bogotá",
            Region: "Cundinamarca",
            Address: "Zona T, Chapinero, Bogotá",
            Description: "Suites ejecutivas sobre la Zona T: salas de reuniones, gimnasio 24 horas y "
                + "desayuno buffet. A una cuadra del Andino y a 20 minutos del aeropuerto por la "
                + "Calle 26 — pensado para el viajero de negocios.",
            Gallery: GalleryFor("bog-zonat"),
            Amenities: new[] { "Salas de reuniones", "Gimnasio 24 horas", "Desayuno buffet según la tarifa", "Wifi de alta velocidad", "Room service", "Parqueadero cubierto" },
            Specs: new[]
            {
                new StaySpec("Habitaciones", "64"),
                new StaySpec("Huéspedes máx. por habitación", "4"),
                new StaySpec("Tamaño promedio", "48 m²"),
                new StaySpec("Check-in", "3:00 p. m."),
                new StaySpec("Check-out", "12:00 m."),
            },
            Geo: new StayGeo(4.6669, -74.0536),
            Reviews: SinOpiniones,
            RoomTypeCodes: new[] { "STE" }),

        new StayDetail(
            StayId: "eje-salento",
            Name: "Finca Niebla Alta",
            City: "Salento",
            Region: "Quindío",
            Address: "Valle de Cocora, Salento",
            Description: "Finca cafetera con vista directa al Valle de Cocora y sus palmas de cera. "
                + "Tour de café propio, chimenea, caminatas guiadas al amanecer y cocina "
                + "tradicional del Eje Cafetero.",
            Gallery: GalleryFor("eje-salento"),
            Amenities: new[] { "Tour de café", "Chimenea", "Caminatas guiadas", "Desayuno típico según la tarifa", "Wifi en zonas comunes", "Parqueadero" },
            Specs: new[]
            {
                new StaySpec("Habitaciones", "8"),
                new StaySpec("Huéspedes máx. por habitación", "4"),
                new StaySpec("Tamaño promedio", "30 m²"),
                new StaySpec("Check-in", "2:00 p. m."),
                new StaySpec("Check-out", "11:00 a. m."),
            },
            Geo: new StayGeo(4.6372, -75.5709),
            Reviews: SinOpiniones,
            RoomTypeCodes: Array.Empty<string>()),

        new StayDetail(
            StayId: "eje-filandia",
            Name: "Hacienda Café & Bruma",
            City: "Filandia",
            Region: "Quindío",
            Address: "Zona rural, Filandia",
            Description: "Hacienda centenaria entre cafetales de Filandia, con balcones de colores, "
                + "spa de montaña y catas de café de origen. El plan perfecto para desconectarse "
                + "en el corazón del Paisaje Cultural Cafetero.",
            Gallery: GalleryFor("eje-filandia"),
            Amenities: new[] { "Spa de montaña", "Catas de café", "Piscina climatizada", "Desayuno según la tarifa", "Wifi en zonas comunes", "Pet friendly" },
            Specs: new[]
            {
                new StaySpec("Habitaciones", "14"),
                new StaySpec("Huéspedes máx. por habitación", "3"),
                new StaySpec("Tamaño promedio", "33 m²"),
                new StaySpec("Check-in", "2:00 p. m."),
                new StaySpec("Check-out", "11:00 a. m."),
            },
            Geo: new StayGeo(4.6747, -75.6584),
            Reviews: SinOpiniones,
            RoomTypeCodes: Array.Empty<string>()),
    };

    public Task<StayDetail?> GetStayAsync(string stayRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stayRef))
        {
            return Task.FromResult<StayDetail?>(null);
        }

        // Acepta offerId del search ("DLX/DLX-FLEX") → resolver por el room type
        // (el segmento antes del '/'). También stayId o room type code directos.
        var key = stayRef.Trim();
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        var head = slash > 0 ? key[..slash] : key;

        var match = Catalog.FirstOrDefault(s =>
                string.Equals(s.StayId, key, StringComparison.OrdinalIgnoreCase))
            ?? Catalog.FirstOrDefault(s =>
                s.RoomTypeCodes.Any(c => string.Equals(c, head, StringComparison.OrdinalIgnoreCase)));

        return Task.FromResult(match);
    }

    // Galería determinista por estadía: cover local coherente con el tema
    // (servido desde wwwroot/media/booking/; el adapter real las leería del CMS/media).
    private static IReadOnlyList<string> GalleryFor(string stayId) => new[]
    {
        $"/media/booking/{stayId}.jpg",
    };
}
