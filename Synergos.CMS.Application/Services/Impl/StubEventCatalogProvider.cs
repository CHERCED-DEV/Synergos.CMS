using System.Collections.Concurrent;
using Synergos.CMS.Application.Configuration;
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

    // Catálogo sembrado (4 eventos, mezcla general/reserved) + eventos publicados
    // por organizadores en runtime (PublishEventAsync). ConcurrentDictionary
    // keyed por id → el estado (eventos creados en la demo) vive en el proceso,
    // igual que el resto de stubs del motor. Sembrado en el ctor estático.
    private static readonly ConcurrentDictionary<string, EventDetail> Catalog = BuildCatalog();

    // Contador monotónico para el id de eventos publicados por organizadores.
    private static int _publishedCounter;

    private readonly ICatalogIndex<EventSummary> _index;

    /// <summary>Ctor por defecto: el motor, sin tope de página (esta seam devuelve todo).</summary>
    public StubEventCatalogProvider()
        : this(new InMemoryCatalogIndex<EventSummary>(Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal StubEventCatalogProvider(ICatalogIndex<EventSummary> index)
        => _index = index ?? throw new ArgumentNullException(nameof(index));

    /// <summary>
    /// Lo que Eventos DECLARA sobre su catálogo. Cero lógica.
    /// </summary>
    internal static CatalogDescriptor<EventSummary> Descriptor { get; } = new(
        idOf: s => s.Id,
        searchFields: new[]
        {
            new CatalogSearchField<EventSummary>(5, s => s.Title),
            // La ciudad discrimina de verdad en un catálogo de eventos ("¿qué hay en
            // Bogotá?" es LA pregunta), así que pesa más que la categoría.
            new CatalogSearchField<EventSummary>(3, s => s.City),
            new CatalogSearchField<EventSummary>(3, s => s.Venue),
            new CatalogSearchField<EventSummary>(2, s => s.Category),
        },
        // El orden histórico: por fecha de inicio. En una agenda, lo próximo va primero.
        defaultOrder: events => events.OrderBy(s => s.StartUtc),
        filters: new CatalogFilter<EventSummary>[]
        {
            new CatalogTermFilter<EventSummary>("category", "Categoría", s => new[] { s.Category }),
            new CatalogTermFilter<EventSummary>("city", "Ciudad", s => new[] { s.City }),
        });

    public Task<IReadOnlyList<EventSummary>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        // Catalog.Values se lee FRESCO en cada búsqueda, y de ahí sale gratis el
        // read-your-writes que este vertical necesita: el organizador publica y lo ve en la
        // misma pantalla. El motor es una función pura sin caché (lo dice su contrato), así
        // que no hay nada que invalidar — justamente lo que un índice asíncrono rompería.
        var results = _index.Search(
            Catalog.Values.Select(e => e.Summary).ToList(),
            // Take explícito + Unpaged en el ctor: esta seam promete TODA la agenda.
            new CatalogQuery(Text: query, Take: int.MaxValue));

        return Task.FromResult(results.Items);
    }

    public Task<EventDetail?> GetEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Task.FromResult<EventDetail?>(null);
        }

        var id = eventId.Trim();
        var match = Catalog.Values.FirstOrDefault(e =>
            string.Equals(e.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Summary.Slug, id, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<EventDetail> PublishEventAsync(EventDetail draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // Id vacío = alta: lo asigna el catálogo, que es quien almacena. Id presente =
        // re-publicación: se respeta y reemplaza (idempotente).
        var published = string.IsNullOrWhiteSpace(draft.Summary.Id)
            ? draft with { Summary = draft.Summary with { Id = NextPublishedId() } }
            : draft;

        Catalog[published.Summary.Id] = published;
        return Task.FromResult(published);
    }

    /// <summary>
    /// El siguiente id estable para un evento publicado por un organizador
    /// (<c>evt-org-N</c>). Determinista dentro del proceso; el adapter real devuelve el
    /// id del CMS/índice en su lugar.
    /// </summary>
    /// <remarks>
    /// Privado a propósito: era <c>public static</c> y el management service se lo llamaba
    /// a la CLASE CONCRETA, saltándose la seam. Ahora nadie fuera de este stub puede
    /// acuñar ids del stub.
    /// </remarks>
    private static string NextPublishedId()
        => $"evt-org-{Interlocked.Increment(ref _publishedCounter)}";

    private static ConcurrentDictionary<string, EventDetail> BuildCatalog()
    {
        var festival = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-festival-estereo",
                Slug: "festival-estereo-2026",
                Title: "Festival Estéreo 2026",
                Category: "Música",
                City: "Bogotá",
                Venue: "Parque Simón Bolívar",
                // 14:00 Bogotá (UTC-5) = la primera fila de la agenda. La pantalla
                // formatea con Intl en la zona del navegador, así que el instante se
                // elige por cómo se LEE en Colombia, no por cómo se escribe en UTC.
                StartUtc: new DateTimeOffset(2026, 8, 15, 19, 0, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/festival-estereo.jpg",
                PriceFrom: 180_000m,
                Currency: Currency,
                Mode: "general",
                Geo: new EventGeo(4.6584, -74.0936)), // Parque Simón Bolívar, Bogotá
            Description: "Dos escenarios, más de 20 artistas nacionales e internacionales y zona gastronómica. La cita musical del año en Bogotá.",
            Organizer: "Estéreo Producciones",
            Tiers: new[]
            {
                new EventTier(
                    "GEN", "General", 180_000m, Currency, Capacity: 5000, Remaining: 1240, MaxPerOrder: 6,
                    Description: "Acceso de pie a los dos escenarios durante toda la jornada, desde las 14:00.",
                    Perks: new[]
                    {
                        "Circulación libre entre el Escenario Norte y el Sur",
                        "Más de 20 puestos de comida y bebida en el predio",
                        "Puntos de agua potable gratuitos en todo el recinto",
                    },
                    SaleWindow: "Hasta el 14 de agosto de 2026"),
                new EventTier(
                    "VIP", "VIP", 420_000m, Currency, Capacity: 800, Remaining: 95, MaxPerOrder: 4,
                    Description: "Zona vallada frente al Escenario Sur, con aforo reducido y servicios aparte.",
                    Perks: new[]
                    {
                        "Plataforma elevada con vista directa, a 15 metros del Escenario Sur",
                        "Barra propia y baños sin fila",
                        "Ingreso por la puerta 1, sin pasar por taquilla",
                        "Casillero con carga para el celular",
                    },
                    SaleWindow: "Hasta el 10 de agosto de 2026",
                    Featured: true),
                new EventTier(
                    "EARLY", "Preventa", 140_000m, Currency, Capacity: 1000, Remaining: 0, MaxPerOrder: 6,
                    Description: "Tarifa de lanzamiento de diciembre, con las mismas condiciones que la entrada General.",
                    Perks: new[]
                    {
                        "40.000 pesos por debajo del precio de lista",
                        "Cupo limitado a 1.000 boletas, sin reposición",
                        "Manilla enviada a domicilio la semana previa al festival",
                    },
                    SaleWindow: "Cerró el 28 de febrero de 2026"),
            },
            SeatMap: null,
            Artist: new EventArtist("Cordillera Eléctrica", "Headliner · fusión andina y electrónica en vivo", 328_000),
            Highlights: new[]
            {
                "Dos escenarios en paralelo, sin cruces de set",
                "Más de 20 artistas nacionales e internacionales",
                "Cierre estelar con Cordillera Eléctrica",
                "Zona gastronómica al aire libre, abierta todo el día",
                "E-ticket QR y check-in ágil en el ingreso",
                "Aforo controlado en el Parque Simón Bolívar",
            },
            Sessions: new[]
            {
                new EventSession("fest-s1", "14:00", "Apertura de puertas y zona de food trucks", ""),
                new EventSession("fest-s2", "16:00", "Bandas emergentes en el Escenario Norte", "La Sonora Bogotá"),
                new EventSession("fest-s3", "18:30", "Cumbia electrónica en el Escenario Sur", "Bruma Tropical"),
                new EventSession("fest-s4", "20:30", "Acto internacional invitado en el Escenario Norte", "Selva Neón"),
                new EventSession("fest-s5", "22:00", "Cierre estelar en el Escenario Sur", "Cordillera Eléctrica"),
            });

        var teatro = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-concierto-sinfonico",
                Slug: "concierto-sinfonico-octubre",
                Title: "Concierto Sinfónico de Octubre",
                Category: "Música",
                City: "Medellín",
                Venue: "Teatro Metropolitano",
                // 4 oct 19:00 Bogotá = apertura de puertas (primera fila de la agenda).
                // OJO: estaba como 4-oct 00:00Z, y medianoche UTC RETROCEDE UN DÍA en
                // toda América — la ficha decía "sábado 3 de octubre" mientras la venta
                // de Balcón cerraba "hasta el 3 de octubre", el mismo día que pintaba.
                StartUtc: new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/sinfonico.jpg",
                PriceFrom: 90_000m,
                Currency: Currency,
                Mode: "reserved",
                Geo: new EventGeo(6.2447, -75.5749)), // Teatro Metropolitano, Medellín
            Description: "La Orquesta Filarmónica interpreta un programa de clásicos. Asientos numerados por zona.",
            Organizer: "Teatro Metropolitano",
            Tiers: new[]
            {
                new EventTier(
                    "PLATEA", "Platea", 180_000m, Currency, Capacity: 12, Remaining: 9, MaxPerOrder: 6, ZoneId: "platea",
                    Description: "Butaca numerada al nivel de la orquesta, en el frente de la sala.",
                    Perks: new[]
                    {
                        "Filas A y B, a menos de diez metros del director",
                        "Programa de mano impreso con las notas del repertorio",
                        "Copa de vino en el intermedio",
                    },
                    SaleWindow: "Hasta el 2 de octubre de 2026",
                    Featured: true),
                new EventTier(
                    "BALCON", "Balcón", 90_000m, Currency, Capacity: 12, Remaining: 12, MaxPerOrder: 6, ZoneId: "balcon",
                    Description: "Segundo nivel, con el escenario completo a la vista.",
                    Perks: new[]
                    {
                        // Filas C y D = exactamente las que trae la zona "balcon" del seat-map.
                        "Filas C y D, en el primer balcón sobre la platea",
                        "Ascensor directo desde el vestíbulo, sin escaleras",
                        "Guardarropa sin costo durante la función",
                    },
                    SaleWindow: "Hasta el 3 de octubre de 2026"),
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
                }),
            Artist: new EventArtist("Orquesta Filarmónica de Medellín", "Filarmónica de Medellín, con dirección invitada de Santiago Villa", 27_800),
            Highlights: new[]
            {
                "Programa de clásicos: Beethoven, Dvořák y Chaikovski",
                "Dirección invitada del maestro Santiago Villa",
                "Asientos numerados por zona: Platea o Balcón",
                "Acústica del Teatro Metropolitano de Medellín",
                "Charla previa gratuita sobre el repertorio",
                "Confirmación inmediata con e-ticket QR",
            },
            Sessions: new[]
            {
                new EventSession("sinf-s1", "19:00", "Apertura de puertas y acomodación por zona", ""),
                new EventSession("sinf-s2", "19:40", "Charla previa: claves del programa", "Santiago Villa"),
                new EventSession("sinf-s3", "20:00", "Primera parte: Beethoven y Dvořák", "Orquesta Filarmónica de Medellín"),
                new EventSession("sinf-s4", "20:50", "Intermedio", ""),
                new EventSession("sinf-s5", "21:10", "Segunda parte y bis: Chaikovski", "Orquesta Filarmónica de Medellín"),
            });

        var conferencia = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-cumbre-tech",
                Slug: "cumbre-tech-2026",
                Title: "Cumbre Tech 2026",
                Category: "Conferencia",
                City: "Bogotá",
                Venue: "Ágora Centro de Convenciones",
                // 08:30 Bogotá = registro y acreditación (primera fila de la agenda).
                StartUtc: new DateTimeOffset(2026, 9, 22, 13, 30, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/cumbre-tech.jpg",
                PriceFrom: 250_000m,
                Currency: Currency,
                Mode: "general",
                Geo: new EventGeo(4.6280, -74.0644)), // Ágora Bogotá
            Description: "Keynotes de líderes de la industria, talleres prácticos y networking. El evento de tecnología más grande del país.",
            Organizer: "Synergos Labs",
            Tiers: new[]
            {
                new EventTier(
                    "STD", "Estándar", 250_000m, Currency, Capacity: 1200, Remaining: 430, MaxPerOrder: 10,
                    Description: "Jornada completa en el auditorio principal, de la apertura al cierre.",
                    Perks: new[]
                    {
                        // Lo que el auditorio DA según la agenda: keynote (09:30) + panel
                        // (11:00). Decía "las cuatro charlas" y la agenda solo tiene dos:
                        // s1 es registro, s4 es taller de plan Pro y s5 es networking.
                        "Keynote de apertura y panel sobre escalar producto en Latinoamérica",
                        "Zona de demos con 18 startups colombianas",
                        "Almuerzo y estación de café incluidos",
                        "Directorio de asistentes para agendar reuniones",
                    },
                    SaleWindow: "Hasta el 18 de septiembre de 2026"),
                new EventTier(
                    "PRO", "Pro", 480_000m, Currency, Capacity: 300, Remaining: 58, MaxPerOrder: 5,
                    Description: "Todo lo del plan Estándar más los cuatro talleres prácticos de la tarde.",
                    Perks: new[]
                    {
                        "Cupo garantizado en el taller que elija, con 25 puestos por sala",
                        "Grabaciones de todas las sesiones por 90 días",
                        "Mesa de trabajo con los ponentes al cierre",
                        "Silla numerada en las primeras cinco filas",
                    },
                    SaleWindow: "Hasta el 12 de septiembre de 2026",
                    Featured: true),
            },
            SeatMap: null,
            Artist: new EventArtist("Valeria Cárdenas", "Keynote principal · VP de Ingeniería, plataforma fintech latam", 41_200),
            Highlights: new[]
            {
                "Keynotes de líderes de producto e ingeniería",
                "Talleres prácticos incluidos con el plan Pro",
                "Networking con más de 2.000 asistentes tech",
                "Demos en vivo de startups colombianas",
                "Ágora Centro de Convenciones, corazón de Bogotá",
                "Certificado digital de asistencia",
            },
            Sessions: new[]
            {
                new EventSession("tech-s1", "08:30", "Registro y acreditación", ""),
                new EventSession("tech-s2", "09:30", "Keynote de apertura: el futuro del software en Colombia", "Valeria Cárdenas"),
                new EventSession("tech-s3", "11:00", "Panel: escalar producto en mercados latam", "Mariana Ospina y Julián Ríos"),
                new EventSession("tech-s4", "13:30", "Talleres prácticos simultáneos (acceso plan Pro)", "Andrés Gómez y equipo Synergos Labs"),
                new EventSession("tech-s5", "16:00", "Networking y cierre", ""),
            });

        var teatroInfantil = new EventDetail(
            Summary: new EventSummary(
                Id: "evt-obra-infantil",
                Slug: "obra-infantil-navidad",
                Title: "Obra Infantil: Cuento de Navidad",
                Category: "Teatro",
                City: "Cali",
                Venue: "Teatro Municipal",
                // 15:30 Bogotá = apertura de puertas (primera fila de la agenda). Estaba
                // en 19:00Z = 14:00 local: la ficha anunciaba el evento HORA Y MEDIA
                // antes de que, según su propia agenda, se abrieran las puertas.
                StartUtc: new DateTimeOffset(2026, 12, 14, 20, 30, 0, TimeSpan.Zero),
                ImageUrl: "/media/eventos/obra-infantil.jpg",
                PriceFrom: 45_000m,
                Currency: Currency,
                Mode: "general",
                Geo: new EventGeo(3.4508, -76.5320)), // Teatro Municipal, Cali
            Description: "Una adaptación familiar del clásico de Dickens, con música en vivo. Apta para todo público.",
            Organizer: "Teatro Municipal de Cali",
            Tiers: new[]
            {
                new EventTier(
                    "GEN", "General", 45_000m, Currency, Capacity: 400, Remaining: 210, MaxPerOrder: 8,
                    Description: "Entrada por persona, con acomodación libre en la sala desde las 15:30.",
                    // Los tres perks anteriores REPETÍAN casi palabra por palabra tres
                    // Highlights de este mismo evento (los 70 minutos, los villancicos y
                    // el encuentro con el elenco), y ambos bloques se pintan en la misma
                    // pantalla. Los Highlights dicen POR QUÉ venir; el tier dice QUÉ
                    // recibe quien compra — que es lo único que la tarjeta aportaba de más.
                    Perks: new[]
                    {
                        "Boleta individual: los niños también ocupan asiento",
                        // "Cambio de fecha" prometía otras funciones: el catálogo modela
                        // UNA fecha por evento, así que la promesa no tenía a dónde ir.
                        "Devolución del 100% hasta 48 horas antes de la función",
                        "Acceso para silla de ruedas y dos puestos reservados",
                    },
                    SaleWindow: "Hasta el 13 de diciembre de 2026",
                    Featured: true),
            },
            SeatMap: null,
            Artist: new EventArtist("Compañía Teatral La Ronda", "Compañía residente · teatro familiar y música en vivo", 8_730),
            Highlights: new[]
            {
                "Adaptación familiar del clásico de Charles Dickens",
                "Villancicos y música orquestal interpretados en vivo",
                "Títeres, proyecciones y nieve escénica",
                "Función de 70 minutos apta desde los 4 años",
                "En la sala histórica del Teatro Municipal de Cali",
                "Encuentro con el elenco al final de la función",
            },
            Sessions: new[]
            {
                new EventSession("obra-s1", "15:30", "Apertura de puertas y acomodación", ""),
                new EventSession("obra-s2", "16:00", "Preludio de villancicos en vivo", "Ensamble del Teatro Municipal"),
                new EventSession("obra-s3", "16:15", "Función: Cuento de Navidad", "Compañía Teatral La Ronda"),
                new EventSession("obra-s4", "17:25", "Encuentro con el elenco y fotos", ""),
            });

        var seeded = new[] { festival, teatro, conferencia, teatroInfantil };
        var map = new ConcurrentDictionary<string, EventDetail>(StringComparer.Ordinal);
        foreach (var e in seeded)
        {
            map[e.Summary.Id] = e;
        }
        return map;
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
