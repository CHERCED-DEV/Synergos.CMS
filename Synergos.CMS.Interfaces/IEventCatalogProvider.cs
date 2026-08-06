namespace Synergos.CMS.Interfaces;

/// <summary>Coordenada del venue de un evento para el mapa/discovery (calca <c>StayGeo</c>).</summary>
public sealed record EventGeo(double Lat, double Lng);

/// <summary>
/// Resumen de un evento para el catálogo/agenda (cara de asistente). Es la
/// unidad que la pantalla de catálogo lista para la búsqueda; el detalle
/// (tiers + seat-map) se resuelve aparte con <see cref="IEventCatalogProvider.GetEventAsync"/>.
/// <see cref="Geo"/> es la coordenada del venue para el mapa de discovery (null si
/// el evento no tiene ubicación geocodificada).
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
    string Mode,
    EventGeo? Geo = null);

/// <summary>
/// Una categoría de ticket reservable de un evento (General / VIP / Early-bird).
/// Es la unidad de selección en modo <c>general</c> (cantidad por tier) y la
/// referencia de precio por zona en modo <c>reserved</c>. <see cref="Remaining"/>
/// es el aforo restante del tier (alimenta el "quedan N").
/// <see cref="Description"/>, <see cref="Perks"/>, <see cref="SaleWindow"/> y
/// <see cref="Featured"/> son la CARA EDITORIAL de la tarjeta de tier: qué incluye,
/// sus inclusiones en viñetas, hasta cuándo se vende y cuál es el recomendado.
/// Opcionales — sin ellos la tarjeta sigue funcionando (precio + aforo), solo sale pobre.
/// </summary>
public sealed record EventTier(
    string Code,
    string Name,
    decimal Price,
    string Currency,
    int Capacity,
    int Remaining,
    int MaxPerOrder,
    string? ZoneId = null,
    // Una línea concreta de qué da este tier (cuerpo de la tarjeta).
    string Description = "",
    // Inclusiones en viñetas (2-4). Null/vacío oculta la lista.
    IReadOnlyList<string>? Perks = null,
    // Cierre de la ventana de venta YA FORMATEADO en es-CO ("Hasta el 12 de julio").
    // Es un texto de presentación, no una fecha: quien conoce el calendario comercial
    // del evento es quien publica el catálogo, no la UI.
    string SaleWindow = "",
    // Marca el tier recomendado. Se espera UNO por evento.
    bool Featured = false);

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
/// El acto/artista protagonista de la ficha (perfil "Artista"). Adaptado al tipo
/// de evento: headliner (música), keynote (conferencia), compañía (teatro), etc.
/// <see cref="Followers"/> es el seguimiento social aproximado (chip "N seguidores").
/// </summary>
public sealed record EventArtist(string Name, string Headline, int Followers);

/// <summary>
/// Una entrada de la agenda del evento (sección "Agenda" de la ficha): hora +
/// qué pasa + quién (ponente/acto). <see cref="Speaker"/> puede ir vacío.
/// </summary>
public sealed record EventSession(string Id, string Time, string Title, string Speaker);

/// <summary>
/// Ficha completa de un evento: el resumen + descripción/organizador + sus tiers
/// (precio/aforo) + el seat-map (si es modo reserved). Es lo que la pantalla de
/// ficha renderiza y desde donde el asistente hace Select. <see cref="Artist"/>,
/// <see cref="Highlights"/> y <see cref="Sessions"/> alimentan los bloques
/// artista / "por qué asistir" / agenda (opcionales — null/vacío los oculta).
/// </summary>
public sealed record EventDetail(
    EventSummary Summary,
    string Description,
    string Organizer,
    IReadOnlyList<EventTier> Tiers,
    EventSeatMap? SeatMap,
    EventArtist? Artist = null,
    IReadOnlyList<string>? Highlights = null,
    IReadOnlyList<EventSession>? Sessions = null)
{
    /// <summary>
    /// Porcentaje del aforo total ya vendido (0..100), para el "¡Últimas localidades!"
    /// y el orden por popularidad del catálogo. <b>Derivado, NO almacenado</b>: sale de
    /// sumar <see cref="EventTier.Capacity"/> y <see cref="EventTier.Remaining"/> de los
    /// <see cref="Tiers"/>, que son la única fuente del aforo. Guardarlo como campo
    /// significaría escribirlo a mano y quedaría mintiendo en cuanto se venda o se
    /// amplíe una entrada — la clase de dato que se desincroniza el primer día.
    /// </summary>
    /// <remarks>
    /// Un evento sin aforo declarado (capacidad 0, p.ej. tiers vacíos o de cupo
    /// ilimitado) devuelve 0 en vez de dividir por cero. El resultado se clampea a
    /// 0..100 para que un <c>Remaining</c> inconsistente (mayor que la capacidad, o
    /// negativo por un sobrecupo) no saque un porcentaje absurdo a la pantalla.
    /// El redondeo es <see cref="MidpointRounding.AwayFromZero"/>: el .5 sube, que es
    /// lo que un lector espera de "redondeado" (el default de .NET es bancario y
    /// dejaría 12,5% en 12% pero 47,5% en 48%, sin criterio visible).
    /// </remarks>
    public int SoldPercent
    {
        get
        {
            var tiers = Tiers;
            if (tiers is null || tiers.Count == 0)
            {
                return 0;
            }

            var capacity = tiers.Sum(t => (long)t.Capacity);
            if (capacity <= 0)
            {
                return 0;
            }

            var remaining = tiers.Sum(t => (long)t.Remaining);
            var sold = capacity - remaining;
            var percent = (int)Math.Round(sold * 100d / capacity, MidpointRounding.AwayFromZero);
            return Math.Clamp(percent, 0, 100);
        }
    }
}

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

    /// <summary>
    /// Publica un evento nuevo (creado por un organizador) en el catálogo:
    /// desde ese momento aparece en <see cref="SearchAsync"/> y su ficha se
    /// resuelve en <see cref="GetEventAsync"/>. Devuelve el detalle publicado
    /// (con su id ya asignado). El adapter real persiste en el índice/CMS;
    /// el stub lo agrega al catálogo en memoria. Es la seam que consume
    /// <see cref="IEventManagementService.CreateEventAsync"/> — la cara de
    /// organizador NO conoce el almacenamiento del catálogo (DIP).
    /// </summary>
    /// <param name="draft">
    /// El evento a publicar. <b>Con <c>Summary.Id</c> vacío, la implementación asigna
    /// uno nuevo</b> (alta); con un id existente, reemplaza (re-publicar es idempotente).
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <remarks>
    /// <b>El id lo asigna QUIEN ALMACENA, y por eso vive detrás de esta seam.</b> Antes el
    /// contrato exigía un id ya puesto pero no ofrecía forma de obtenerlo, así que el único
    /// llamador se lo pedía al <c>static</c> de la clase concreta del stub. Eso volvía la
    /// seam mentirosa: registrar otro <see cref="IEventCatalogProvider"/> NO desconectaba el
    /// stub —los ids seguían saliendo de su contador— y el swap quedaba parcial y en
    /// silencio. Un adapter CMS/índice devuelve aquí SU id nativo.
    /// </remarks>
    Task<EventDetail> PublishEventAsync(EventDetail draft, CancellationToken cancellationToken = default);
}
