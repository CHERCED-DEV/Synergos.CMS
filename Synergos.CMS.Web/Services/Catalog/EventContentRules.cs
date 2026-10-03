using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// Gravedad de un problema encontrado al proyectar contenido de eventos.
/// </summary>
public enum EventContentIssueLevel
{
    /// <summary>Dato incompleto u opcional que falta: se sirve el resto.</summary>
    Warning,

    /// <summary>Dato contradictorio que el editor tiene que corregir.</summary>
    Error,
}

/// <summary>Un problema concreto, listo para que el llamador lo loguee.</summary>
public sealed record EventContentIssue(EventContentIssueLevel Level, string Message);

/// <summary>Lo que el editor escribió en una localidad, sin interpretar.</summary>
public sealed record EventTierContent(
    string? Code,
    string? Name,
    int Price,
    int Capacity,
    int MaxPerOrder,
    string? ZoneId = null,
    string? Description = null,
    IReadOnlyList<string>? Perks = null,
    string? SaleWindow = null,
    bool Featured = false,
    // Los dos días de la ventana de venta, tal como los eligió el editor (sin hora ni zona).
    DateTime? SaleOpens = null,
    DateTime? SaleCloses = null);

/// <summary>Lo que el editor escribió en una zona, sin interpretar.</summary>
public sealed record EventZoneContent(
    string? Id,
    string? Name,
    string? TierCode,
    int Price,
    IReadOnlyList<string>? RowLabels,
    int SeatsPerRow);

/// <summary>Lo que el editor escribió en un punto de agenda, sin interpretar.</summary>
public sealed record EventSessionContent(string? Time, string? Title, string? Speaker);

/// <summary>El resultado de proyectar, con los problemas encontrados por el camino.</summary>
/// <typeparam name="T">Lo proyectado.</typeparam>
public sealed record EventContentResult<T>(T Value, IReadOnlyList<EventContentIssue> Issues);

/// <summary>
/// Las reglas de negocio que convierten lo que el editor autoró en una ficha comprable.
/// </summary>
/// <remarks>
/// <b>Es una clase PURA a propósito.</b> Vive junto al lector de contenido porque solo tiene
/// sentido para esta forma de contenido, pero no toca un solo tipo de Umbraco: recibe records
/// planos y devuelve records de dominio. Esa es la única razón por la que las reglas que
/// deciden si un evento se puede comprar —códigos repetidos, aforo, zonas colgando de una
/// localidad inexistente, el techo de asientos generados— son verificables sin levantar un
/// <c>IPublishedContent</c>, que en la práctica no se puede simular (<c>Value&lt;T&gt;</c> es un
/// método de extensión sobre una cadena de propiedades y fallbacks).
///
/// <para>Tampoco loguea: devuelve los problemas y el llamador —que sí tiene
/// <c>ILogger</c>— los emite. Mismo criterio que <c>DefaultSynHostEmitter</c>.</para>
///
/// <para><b>Todas las reglas omiten, ninguna lanza.</b> Una ficha con una localidad mal escrita
/// tiene que vender las otras. Lo que no puede pasar nunca es servir algo plausible pero
/// equivocado: ahí se descarta con un error, porque un aforo o un precio mal servidos los
/// descubre el asistente en la puerta.</para>
/// </remarks>
public static class EventContentRules
{
    /// <summary>Tope de entradas por compra cuando el editor no lo declara.</summary>
    public const int DefaultMaxPerOrder = 10;

    /// <summary>Modo de venta con asiento elegido en un mapa.</summary>
    /// <remarks>
    /// Nombrado y no literal porque el vocabulario ya se usa en dos reglas —resolver el modo y
    /// derivar los chips— y son las dos caras de la misma decisión: si divergen, la tarjeta
    /// promete "asientos numerados" sobre un evento que se vende por cantidad.
    /// </remarks>
    public const string ReservedMode = "reserved";

    /// <summary>Modo de venta por cantidad, sin asiento asignado. Es el modo por defecto.</summary>
    public const string GeneralMode = "general";

    /// <summary>
    /// Techo de asientos generados por evento.
    /// </summary>
    /// <remarks>
    /// Los asientos no se autoran uno por uno: salen de filas × butacas. Eso es lo que hace el
    /// modelo usable (un teatro de 40 filas se declara en dos campos) y también lo que lo hace
    /// peligroso — <c>seatsPerRow = 10000</c> es un dígito de más y son diez mil objetos por
    /// fila, serializados en cada request de la ficha.
    /// </remarks>
    public const int MaxSeatsPerEvent = 20_000;

    /// <summary>
    /// Las localidades servibles, en el orden en que el editor las puso.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Sin código o sin nombre: no hay nada que identificar ni que mostrar.</item>
    /// <item><b>Código repetido:</b> se queda la PRIMERA. El código es lo que el checkout usa
    /// para resolver precio y aforo; con dos "vip" a distinto precio, cuál cobra depende del
    /// orden de un diccionario y el comprador paga lo que salga.</item>
    /// <item>Aforo 0 o negativo: no hay qué vender, y una tarjeta agotada desde el minuto cero
    /// confunde más de lo que informa.</item>
    /// <item>Precio negativo: no es un descuento, es una errata que le pagaría al comprador.</item>
    /// <item>El tope por compra se recorta al aforo: ofrecer "hasta 10" sobre una localidad de
    /// 4 es prometer seis entradas que no existen.</item>
    /// <item>Solo UNA recomendada: si el editor marcó varias se respeta la primera, que es lo
    /// que dice la ayuda del campo.</item>
    /// <item><b>La ventana de venta</b> (#195): «Venta desde» abre al EMPEZAR ese día y «Venta
    /// hasta» cierra al empezar el día SIGUIENTE —el último día se vende entero—, los dos en la
    /// zona del sitio. Una ventana al revés (cierra antes de abrir) no se puede vender nunca: la
    /// localidad se omite con un error, igual que un código repetido.</item>
    /// </list>
    ///
    /// <para><b><see cref="EventTier.Remaining"/> sale igual al aforo, y es deliberado:</b> el
    /// contenido declara CUÁNTO hay, no cuánto queda. Lo vendido lo sabe el ledger de reservas
    /// del motor de ticketing, que esta capa no ve ni debe ver — un catálogo de contenido que
    /// intentara contar ventas quedaría desincronizado el primer día.</para>
    /// </remarks>
    /// <param name="zonaDelSitio">En qué zona son los días de la ventana. Sin ella, «hasta el 14»
    /// no dice cuándo termina el 14.</param>
    public static EventContentResult<IReadOnlyList<EventTier>> BuildTiers(
        string slug,
        IReadOnlyList<EventTierContent>? drafts,
        string currency,
        TimeZoneInfo zonaDelSitio)
    {
        var issues = new List<EventContentIssue>();
        if (drafts is null || drafts.Count == 0)
        {
            return new EventContentResult<IReadOnlyList<EventTier>>(Array.Empty<EventTier>(), issues);
        }

        var tiers = new List<EventTier>(drafts.Count);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var draft in drafts)
        {
            var code = Normalize(draft.Code);
            var name = draft.Name?.Trim();

            if (code.Length == 0 || string.IsNullOrEmpty(name))
            {
                issues.Add(Warn($"Evento '{slug}': una localidad no tiene código o nombre; se omite."));
                continue;
            }

            if (!seenCodes.Add(code))
            {
                issues.Add(Err(
                    $"Evento '{slug}': el código de localidad '{code}' está repetido. Se conserva la primera " +
                    "y se omiten las demás: el checkout resuelve precio y aforo por ese código."));
                continue;
            }

            if (draft.Capacity <= 0)
            {
                issues.Add(Warn(
                    $"Evento '{slug}': la localidad '{code}' tiene aforo {draft.Capacity}; no se pone a la venta."));
                continue;
            }

            if (draft.Price < 0)
            {
                issues.Add(Err(
                    $"Evento '{slug}': la localidad '{code}' tiene precio negativo ({draft.Price}); se omite."));
                continue;
            }

            var (abre, cierra) = Ventana(slug, code, draft, zonaDelSitio, issues);
            if (abre is not null && cierra is not null && cierra <= abre)
            {
                issues.Add(Err(
                    $"Evento '{slug}': la localidad '{code}' deja de venderse ({draft.SaleCloses:yyyy-MM-dd}) antes de " +
                    $"empezar a venderse ({draft.SaleOpens:yyyy-MM-dd}); se omite. Revisa «Venta desde» y «Venta hasta»."));
                continue;
            }

            var maxPerOrder = draft.MaxPerOrder > 0 ? draft.MaxPerOrder : DefaultMaxPerOrder;
            maxPerOrder = Math.Min(maxPerOrder, draft.Capacity);

            var zoneId = Normalize(draft.ZoneId);

            tiers.Add(new EventTier(
                Code: code,
                Name: name,
                Price: draft.Price,
                Currency: currency,
                Capacity: draft.Capacity,
                Remaining: draft.Capacity,
                MaxPerOrder: maxPerOrder,
                ZoneId: zoneId.Length == 0 ? null : zoneId,
                Description: draft.Description?.Trim() ?? string.Empty,
                Perks: CleanTextList(draft.Perks),
                SaleWindow: draft.SaleWindow?.Trim() ?? string.Empty,
                Featured: draft.Featured,
                SaleOpensUtc: abre,
                SaleClosesUtc: cierra));
        }

        var featuredCount = tiers.Count(t => t.Featured);
        if (featuredCount > 1)
        {
            var kept = tiers.First(t => t.Featured).Code;
            issues.Add(Warn(
                $"Evento '{slug}': {featuredCount} localidades marcadas como recomendadas; se conserva '{kept}'."));

            var seenFeatured = false;
            for (var i = 0; i < tiers.Count; i++)
            {
                if (!tiers[i].Featured)
                {
                    continue;
                }

                if (seenFeatured)
                {
                    tiers[i] = tiers[i] with { Featured = false };
                }

                seenFeatured = true;
            }
        }

        return new EventContentResult<IReadOnlyList<EventTier>>(tiers, issues);
    }

    /// <summary>
    /// El mapa de asientos, o null si el evento no vende asiento numerado.
    /// </summary>
    /// <remarks>
    /// Cada zona tiene que apuntar a una localidad EXISTENTE: el aforo y el precio salen de
    /// ahí, y una zona colgando de un código que nadie declaró vendería asientos que el
    /// checkout no sabe cobrar.
    ///
    /// <para>Precio 0 en la zona significa "cobra lo mismo que su localidad". Es el default
    /// útil: la mayoría de zonas no tienen precio propio, y obligar a repetirlo garantiza que
    /// un día se cambie el de la localidad y no el de la zona.</para>
    ///
    /// <para>El id de cada asiento lleva la zona porque dos zonas del mismo recinto pueden
    /// tener una fila "A". Sin el prefijo, apartar A1 en Platea apartaría A1 en Palco.</para>
    ///
    /// <para>Todas las butacas nacen <c>free</c>: igual que con el aforo, lo ocupado lo sabe el
    /// ledger de reservas, no el contenido.</para>
    /// </remarks>
    public static EventContentResult<EventSeatMap?> BuildSeatMap(
        string slug,
        IReadOnlyList<EventZoneContent>? drafts,
        IReadOnlyList<EventTier> tiers,
        string currency,
        string venueName)
    {
        var issues = new List<EventContentIssue>();
        if (drafts is null || drafts.Count == 0)
        {
            return new EventContentResult<EventSeatMap?>(null, issues);
        }

        var byCode = new Dictionary<string, EventTier>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in tiers)
        {
            byCode[tier.Code] = tier;
        }

        var zones = new List<EventZone>(drafts.Count);
        var seenZoneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seatBudget = MaxSeatsPerEvent;

        foreach (var draft in drafts)
        {
            var zoneId = Normalize(draft.Id);
            var zoneName = draft.Name?.Trim();
            var tierCode = Normalize(draft.TierCode);

            if (zoneId.Length == 0 || string.IsNullOrEmpty(zoneName) || tierCode.Length == 0)
            {
                issues.Add(Warn($"Evento '{slug}': una zona no tiene código, nombre o localidad; se omite."));
                continue;
            }

            if (!seenZoneIds.Add(zoneId))
            {
                issues.Add(Err($"Evento '{slug}': el código de zona '{zoneId}' está repetido; se conserva la primera."));
                continue;
            }

            if (!byCode.TryGetValue(tierCode, out var tier))
            {
                issues.Add(Err(
                    $"Evento '{slug}': la zona '{zoneId}' apunta a la localidad '{tierCode}', que no existe en " +
                    "la pestaña Entradas. La zona se omite: sin localidad no hay precio ni aforo que cobrar."));
                continue;
            }

            var rowLabels = CleanTextList(draft.RowLabels);
            if (rowLabels.Count == 0 || draft.SeatsPerRow <= 0)
            {
                issues.Add(Warn(
                    $"Evento '{slug}': la zona '{zoneId}' no tiene filas o no tiene butacas por fila " +
                    $"({draft.SeatsPerRow}); se omite."));
                continue;
            }

            var seatCount = (long)rowLabels.Count * draft.SeatsPerRow;
            if (seatCount > seatBudget)
            {
                issues.Add(Err(
                    $"Evento '{slug}': la zona '{zoneId}' pide {seatCount} asientos y quedan {seatBudget} del tope " +
                    $"de {MaxSeatsPerEvent} por evento. La zona se OMITE entera — servirla a medias vendería " +
                    $"asientos que no existen. Revisa filas ({rowLabels.Count}) × butacas por fila " +
                    $"({draft.SeatsPerRow})."));
                continue;
            }

            seatBudget -= (int)seatCount;

            var price = draft.Price;
            if (price < 0)
            {
                issues.Add(Err(
                    $"Evento '{slug}': la zona '{zoneId}' tiene precio negativo ({price}); se cobra el de su " +
                    $"localidad ({tier.Price})."));
                price = 0;
            }

            var effectivePrice = price > 0 ? price : tier.Price;

            var rows = new List<EventRow>(rowLabels.Count);
            foreach (var rowLabel in rowLabels)
            {
                var seats = new List<EventSeat>(draft.SeatsPerRow);
                for (var seat = 1; seat <= draft.SeatsPerRow; seat++)
                {
                    var label = $"{rowLabel}{seat}";
                    seats.Add(new EventSeat($"{zoneId}-{label}", label, "free"));
                }

                rows.Add(new EventRow(rowLabel, seats));
            }

            zones.Add(new EventZone(zoneId, zoneName, effectivePrice, currency, tier.Code, rows));
        }

        if (zones.Count == 0)
        {
            // Zonas declaradas pero ninguna servible: el evento NO es de asiento numerado hoy.
            // Devolver un mapa vacío dejaría la ficha con un selector sin nada que seleccionar.
            return new EventContentResult<EventSeatMap?>(null, issues);
        }

        return new EventContentResult<EventSeatMap?>(new EventSeatMap(venueName, zones), issues);
    }

    /// <summary>
    /// La agenda ordenada por hora. Los puntos sin hora o sin título se omiten.
    /// </summary>
    /// <remarks>
    /// El orden lo pone ESTA capa y no el editor: la agenda se lee de arriba abajo y una hora
    /// fuera de sitio se lee como un error del sitio, no de quien la escribió. El schema exige
    /// <c>HH:MM</c> con una expresión de validación, así que ordenar por texto ordena por hora.
    /// </remarks>
    public static EventContentResult<IReadOnlyList<EventSession>> BuildSessions(
        string slug,
        IReadOnlyList<EventSessionContent>? drafts)
    {
        var issues = new List<EventContentIssue>();
        if (drafts is null || drafts.Count == 0)
        {
            return new EventContentResult<IReadOnlyList<EventSession>>(Array.Empty<EventSession>(), issues);
        }

        var sessions = new List<EventSession>(drafts.Count);
        foreach (var draft in drafts)
        {
            var time = draft.Time?.Trim();
            var title = draft.Title?.Trim();

            if (string.IsNullOrEmpty(time) || string.IsNullOrEmpty(title))
            {
                issues.Add(Warn($"Evento '{slug}': un punto de agenda no tiene hora o título; se omite."));
                continue;
            }

            sessions.Add(new EventSession(
                Id: $"{slug}-s{sessions.Count + 1}",
                Time: time,
                Title: title,
                Speaker: draft.Speaker?.Trim() ?? string.Empty));
        }

        var ordered = sessions
            .OrderBy(s => s.Time, StringComparer.Ordinal)
            .ToList();

        return new EventContentResult<IReadOnlyList<EventSession>>(ordered, issues);
    }

    /// <summary>
    /// El modo de venta REAL: <c>reserved</c> si hay mapa servible, <c>general</c> si no.
    /// </summary>
    /// <remarks>
    /// <b>Lo que hay manda sobre lo declarado.</b> Un evento marcado <c>reserved</c> sin zonas
    /// servibles dejaría al asistente en una pantalla de mapa vacía, sin forma de comprar; uno
    /// marcado <c>general</c> que sí trae mapa se vendería por cantidad, repartiendo asientos
    /// que el comprador creía elegir.
    /// </remarks>
    public static EventContentResult<string> ResolveMode(string slug, string declaredMode, bool hasSeatMap)
    {
        var actual = hasSeatMap ? ReservedMode : GeneralMode;
        var issues = new List<EventContentIssue>();

        if (!string.Equals(declaredMode, actual, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Warn(
                $"Evento '{slug}': declara modo '{declaredMode}' pero sus datos son de '{actual}' " +
                $"(zonas servibles: {hasSeatMap}). Se sirve como '{actual}'."));
        }

        return new EventContentResult<string>(actual, issues);
    }

    /// <summary>
    /// El perfil del acto, o null si no hay nombre (la ficha oculta el bloque).
    /// </summary>
    /// <remarks>
    /// El nombre es lo único imprescindible: un perfil sin nombre no es un perfil. Un número de
    /// seguidores negativo se sirve como 0, que la ficha ya sabe ocultar.
    /// </remarks>
    public static EventArtist? BuildArtist(string? name, string? headline, int followers)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return new EventArtist(trimmed, headline?.Trim() ?? string.Empty, followers > 0 ? followers : 0);
    }

    /// <summary>
    /// Los renglones no vacíos de un campo de texto repetible.
    /// </summary>
    /// <remarks>
    /// Se recortan y se descartan los blancos: un Enter de más en el backoffice se convierte,
    /// si no, en una viñeta vacía en la tarjeta.
    /// </remarks>
    /// <summary>
    /// El subtítulo de la tarjeta (<c>event.subtitle</c>): sede y ciudad.
    /// </summary>
    /// <remarks>
    /// Sin sede se emite solo la ciudad, y no <c>" · Bogotá"</c>: el separador colgante es el
    /// mismo síntoma que ya se corrigió en la tarjeta de inmuebles, y sale en cuanto un evento
    /// online no tiene venue — que es lo normal, no la excepción.
    /// </remarks>
    public static string BuildSubtitle(string? venue, string? city)
    {
        var v = venue?.Trim() ?? string.Empty;
        var c = city?.Trim() ?? string.Empty;
        if (v.Length == 0) return c;
        if (c.Length == 0) return v;
        return $"{v} · {c}";
    }

    /// <summary>
    /// El estado de ciclo de vida del resumen (<c>event.status</c>): <c>past</c> o
    /// <c>upcoming</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>El contrato de la UI tiene cuatro valores</b> —upcoming, on-sale, sold-out,
    /// past— y aquí solo se pueden emitir dos: <c>EventSummary</c> no lleva aforo, así que la
    /// única dimensión con fuente es la temporal. Emitir <c>on-sale</c> sin saber si queda
    /// cupo sería adivinar, y <c>sold-out</c> sería mentir. Los otros dos los emite la ficha,
    /// que sí conoce el aforo.</para>
    ///
    /// <para><b>El reloj entra por parámetro</b> y no se lee de <c>UtcNow</c> adentro. Esa era
    /// justamente la razón por la que esta regla no tenía test: el límite past/upcoming solo
    /// se puede verificar si se puede parar el reloj encima de él.</para>
    /// </remarks>
    public static string BuildStatus(DateTimeOffset startUtc, DateTimeOffset now)
        => startUtc <= now ? "past" : "upcoming";

    /// <summary>
    /// Los dos extremos de la ventana de venta, como instantes. <c>null</c> es «sin límite por ese
    /// lado».
    /// </summary>
    /// <remarks>
    /// <para><b>Los extremos del date picker son centinelas de «sin límite»</b>: «Venta hasta»
    /// 31-12-9999 es «no cierra», y «Venta desde» 01-01-0001, «abierta desde siempre». Se leen así,
    /// con un aviso. El cierre se calcula como el inicio del día SIGUIENTE al último, y el
    /// siguiente al 31-12-9999 no existe: la excepción no la capturaba nadie y tumbaba la
    /// proyección de TODOS los eventos del sitio —el listado, cada ficha y el checkout—.</para>
    ///
    /// <para>Quitados los dos extremos, cualquier otro día cabe en cualquier zona (los desfases no
    /// pasan de ±14 h), así que acá no queda ninguna fecha que pueda lanzar.</para>
    /// </remarks>
    private static (DateTimeOffset? Abre, DateTimeOffset? Cierra) Ventana(
        string slug,
        string code,
        EventTierContent draft,
        TimeZoneInfo zonaDelSitio,
        List<EventContentIssue> issues)
    {
        DateTimeOffset? abre = null;
        if (draft.SaleOpens is { } desde)
        {
            if (desde.Date == DateTime.MinValue.Date)
            {
                issues.Add(Warn(
                    $"Evento '{slug}': la localidad '{code}' tiene «Venta desde» {desde:yyyy-MM-dd}; se lee como " +
                    "venta sin fecha de apertura."));
            }
            else
            {
                abre = InicioDelDia(desde, zonaDelSitio);
            }
        }

        DateTimeOffset? cierra = null;
        if (draft.SaleCloses is { } hasta)
        {
            if (hasta.Date == DateTime.MaxValue.Date)
            {
                issues.Add(Warn(
                    $"Evento '{slug}': la localidad '{code}' tiene «Venta hasta» {hasta:yyyy-MM-dd}; se lee como " +
                    "venta sin fecha de cierre."));
            }
            else
            {
                cierra = InicioDelDia(hasta.Date.AddDays(1), zonaDelSitio);
            }
        }

        return (abre, cierra);
    }

    /// <summary>
    /// La hora que tecleó el editor, anclada en la zona del sitio.
    /// </summary>
    /// <remarks>
    /// <para><c>Umbraco.DateTime</c> guarda un <see cref="DateTime"/> sin zona (Kind Unspecified) y el
    /// editor teclea hora local: sin anclarla, .NET la leería como hora del SERVIDOR y la agenda se
    /// correría cuando el host no esté en el sitio.</para>
    ///
    /// <para><b>Se anclaba con un UTC−5 fijo</b>, correcto para Colombia —que no tiene horario de
    /// verano— y equivocado para cualquier otro sitio aunque su configuración dijera otra zona. Hoy
    /// es la zona de <c>Synergos:Listados:ZonaHoraria</c>, la misma que ya dice el día de los
    /// listados, y el desfase sale de la zona Y de la fecha. Una hora que el cambio de hora se salta
    /// toma el desfase estándar en vez de lanzar: una ficha mal escrita no tumba la agenda.</para>
    ///
    /// <para>Vive acá y no en la fuente de contenido porque la usan dos reglas: el inicio del evento
    /// y los días de la ventana de venta (#195).</para>
    ///
    /// <para>Lanza si la hora no cabe en un <see cref="DateTimeOffset"/> (ver
    /// <see cref="TryInicioEnLaZona"/>): quien la recibe de un editor o de una petición usa la
    /// versión <c>Try</c>.</para>
    /// </remarks>
    public static DateTimeOffset InicioEnLaZona(DateTime delEditor, TimeZoneInfo zona)
        => TryInicioEnLaZona(delEditor, zona, out var inicio)
            ? inicio
            : throw new ArgumentOutOfRangeException(
                nameof(delEditor), delEditor, "La hora, llevada a UTC, cae fuera del calendario de .NET.");

    /// <summary>
    /// <see cref="InicioEnLaZona"/> sin lanzar: <c>false</c> cuando la hora, llevada a UTC, cae
    /// fuera del calendario de .NET.
    /// </summary>
    /// <remarks>
    /// <para><b>Lo que no cabe existe:</b> las 23:00 del 31-12-9999 en Bogotá son las 04:00 del año
    /// 10000 en UTC. Un date picker deja elegir ese día y una petición puede mandarlo, y en los dos
    /// casos lanzar es un 500 —en la proyección, para todos los eventos del sitio—.</para>
    ///
    /// <para><b>Una hora que el cambio de hora REPITE</b> (el reloj se atrasa y esa hora pasa dos
    /// veces) se lee como la PRIMERA vez, que es la que vive quien la tecleó: el desfase mayor de
    /// los dos. <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> devuelve el estándar —la segunda—
    /// y corría una hora el inicio del evento o del día; en La Habana, la medianoche del
    /// 1-nov-2026.</para>
    /// </remarks>
    public static bool TryInicioEnLaZona(DateTime delEditor, TimeZoneInfo zona, out DateTimeOffset inicio)
    {
        ArgumentNullException.ThrowIfNull(zona);
        var local = DateTime.SpecifyKind(delEditor, DateTimeKind.Unspecified);
        var desfase = zona.IsAmbiguousTime(local)
            ? zona.GetAmbiguousTimeOffsets(local).Max()
            : zona.GetUtcOffset(local);

        var ticksUtc = local.Ticks - desfase.Ticks;
        if (ticksUtc < DateTime.MinValue.Ticks || ticksUtc > DateTime.MaxValue.Ticks)
        {
            inicio = default;
            return false;
        }

        inicio = new DateTimeOffset(local, desfase);
        return true;
    }

    /// <summary>
    /// El instante en que EMPIEZA un día del calendario en la zona del sitio: la medianoche de allá.
    /// </summary>
    /// <remarks>
    /// Un día del date picker llega a medianoche, pero se descarta la hora igual: lo que el editor
    /// eligió es un día, no un instante.
    /// </remarks>
    public static DateTimeOffset InicioDelDia(DateTime dia, TimeZoneInfo zona)
        => InicioEnLaZona(dia.Date, zona);

    /// <summary>
    /// Los chips del resumen (<c>event.badges</c>), derivados del modo de venta.
    /// </summary>
    /// <remarks>
    /// El modo es el ÚNICO campo del resumen con fuente para un chip, y describe algo que al
    /// comprador le cambia la decisión: si escoge silla o entra por orden de llegada. Cualquier
    /// valor que no sea <c>reserved</c> —incluido uno vacío o desconocido— cae a entrada
    /// general, que es el modo por defecto del vertical.
    /// </remarks>
    public static IReadOnlyList<string> BuildBadges(string? mode)
        => string.Equals(mode, ReservedMode, StringComparison.OrdinalIgnoreCase)
            ? new[] { "Asientos numerados" }
            : new[] { "Entrada general" };

    public static IReadOnlyList<string> CleanTextList(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            return Array.Empty<string>();
        }

        var cleaned = new List<string>(raw.Count);
        foreach (var value in raw)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                cleaned.Add(trimmed);
            }
        }

        return cleaned;
    }

    private static string Normalize(string? raw)
        => raw?.Trim().ToLowerInvariant() ?? string.Empty;

    private static EventContentIssue Warn(string message)
        => new(EventContentIssueLevel.Warning, message);

    private static EventContentIssue Err(string message)
        => new(EventContentIssueLevel.Error, message);
}
