using System.Globalization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// La fuente de catálogo de Eventos respaldada por el CONTENIDO del CMS: sirve los
/// <c>eventPage</c> que el editor autoró, en vez del seed hardcodeado.
/// </summary>
/// <remarks>
/// <b>Calco literal de <see cref="UmbracoProductCatalogSource"/></b> (T5 Ola A, ADR 0107),
/// aplicado al segundo vertical. Mismo hueco que cierra: hoy la agenda que se puede COMPRAR
/// está sembrada en C# y la que el editor publica está inerte — los dos universos no comparten
/// un solo slug.
///
/// <para>Es la ÚNICA clase de Eventos que toca Umbraco. El motor y el descriptor siguen en
/// Application, sin saber que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Events = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redeploy.</para>
///
/// <para><b>Emite las DOS caras del catálogo.</b> Como <see cref="ICatalogSource{T}"/> de
/// <see cref="EventSummary"/> sirve la agenda que el buscador lista; como fuente de
/// <see cref="EventDetail"/> sirve la ficha comprable — localidades, aforo y mapa de
/// asientos. La segunda cara llegó con la rebanada que modeló los tiers como contenido
/// (<c>elementEventTier</c> / <c>elementEventZone</c> / <c>elementEventSession</c>): hasta
/// entonces el flag <c>cms</c> construía esta clase pero la ficha seguía saliendo del stub,
/// así que ponerlo en <c>cms</c> no cambiaba nada de lo que el asistente veía.</para>
///
/// <para><b>Las dos caras se proyectan del MISMO recorrido</b> y con las mismas reglas de
/// omisión, para que no exista un evento que salga en la búsqueda y cuya ficha sea 404.</para>
/// </remarks>
public sealed class UmbracoEventCatalogSource : ICatalogSource<EventSummary>, ICatalogSource<EventDetail>
{
    internal const string Vertical = "Events";
    private const string EventPageAlias = "eventPage";
    private const string SiteRootAlias = "siteRoot";

    /// <summary>
    /// Moneda de los precios del catálogo. Constante y no una propiedad de schema: un deploy
    /// es un origen (no hay multi-tenant) y todo el motor de Eventos ya emite COP. El día que
    /// haya un segundo país esto sube a config, no a un TextBox por evento —dejarlo escribir
    /// al editor garantiza "cop"/"COP "/"pesos" en la misma agenda.
    /// </summary>
    private const string Currency = "COP";

    /// <summary>
    /// Colombia no tiene horario de verano, así que el desfase es constante y se puede fijar.
    /// <c>Umbraco.DateTime</c> guarda un <see cref="DateTime"/> sin zona (Kind Unspecified) y
    /// el editor teclea hora local — sin anclarlo aquí, .NET lo leería como hora del SERVIDOR
    /// y la agenda se correría cuando el host no esté en Bogotá.
    /// </summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ILogger<UmbracoEventCatalogSource> _logger;

    public UmbracoEventCatalogSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ILogger<UmbracoEventCatalogSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en la fuente de Tienda: el scope de este catálogo no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Events</c>. Tomarlo del
    /// parámetro obligaría a cada llamador a acertar, y el que se equivoque sirve la agenda
    /// de otro siteRoot en silencio.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    Task<IReadOnlyList<EventSummary>> ICatalogSource<EventSummary>.GetAllAsync(string? scope, CancellationToken cancellationToken)
        => GetAllAsync(scope, cancellationToken);

    /// <summary>
    /// La agenda COMPRABLE: la misma lista, con localidades, agenda y mapa de asientos.
    /// </summary>
    /// <remarks>
    /// Se apoya en el mismo recorrido y las mismas omisiones que el resumen, así que un evento
    /// que aparece en la búsqueda siempre tiene ficha, y uno omitido no aparece en ninguna de
    /// las dos.
    /// </remarks>
    Task<IReadOnlyList<EventDetail>> ICatalogSource<EventDetail>.GetAllAsync(string? scope, CancellationToken cancellationToken)
    {
        var nodes = ResolveNodes();
        var details = nodes
            .Select(ProjectDetail)
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        LogSkipped(nodes.Count, details.Count);
        return Task.FromResult<IReadOnlyList<EventDetail>>(details);
    }

    public Task<IReadOnlyList<EventSummary>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        var events = nodes
            .Select(Project)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        LogSkipped(nodes.Count, events.Count);
        return Task.FromResult<IReadOnlyList<EventSummary>>(events);
    }

    /// <summary>
    /// Los <c>eventPage</c> publicados bajo el siteRoot configurado, o vacío si falta el
    /// contexto o el scope. Es el ÚNICO recorrido: las dos proyecciones parten de aquí.
    /// </summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            // Fuera de un contexto de Umbraco no hay contenido que servir. Agenda vacía y no
            // una excepción: la pantalla se degrada, no revienta.
            _logger.LogWarning("UmbracoEventCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = ResolveBrandKey();
        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // SIN scope NO se sirve nada. Fallar cerrado y ruidoso es lo correcto para un
            // error de configuración: servir sin acotar mezclaría la agenda de todos los
            // siteRoots, en silencio.
            _logger.LogError(
                "UmbracoEventCatalogSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoEventCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(EventPageAlias).ToList();
    }

    /// <summary>
    /// Una línea al final con cuántos nodos se omitieron, no una advertencia por nodo.
    /// </summary>
    /// <remarks>
    /// "Se omitieron 7 de 24" es lo que hace ver que la siembra está mal; siete advertencias
    /// sueltas entre el ruido del log, no. El detalle de POR QUÉ se omitió cada uno ya salió
    /// en su propio log dentro de la proyección.
    /// </remarks>
    private void LogSkipped(int total, int served)
    {
        var skipped = total - served;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "UmbracoEventCatalogSource: se omitieron {Skipped} de {Total} eventPage por datos incompletos.",
                skipped, total);
        }
    }

    private string? ResolveBrandKey()
        => _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

    /// <summary>
    /// <c>eventPage</c> → <see cref="EventSummary"/>. Devuelve null para lo que no es una
    /// entrada de agenda servible.
    /// </summary>
    private EventSummary? Project(IPublishedContent node)
    {
        var slug = node.Value<string>("eventSlug")?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            // Sin slug no hay identidad: es lo que GetEventAsync resuelve y lo que la URL de
            // la ficha lleva. Un evento sin él es un enlace roto.
            _logger.LogWarning("UmbracoEventCatalogSource: eventPage id={Id} sin eventSlug; se omite.", node.Id);
            return null;
        }

        var title = node.Value<string>("eventTitle")?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            // A diferencia del producto (que cae al Name del nodo), aquí NO se inventa un
            // título: el Name es el nombre del nodo en el árbol del backoffice y sale cosas
            // como "Evento (1)" en la tarjeta. Que falte la ficha se ve; que salga con un
            // título de andamiaje no.
            _logger.LogWarning(
                "UmbracoEventCatalogSource: eventPage slug='{Slug}' sin eventTitle; se omite.", slug);
            return null;
        }

        if (!TryParsePriceFrom(node, slug, out var priceFrom))
        {
            return null;
        }

        var start = node.Value<DateTime>("eventStart");
        if (start == default)
        {
            // La fecha es el ORDEN de la agenda ("lo próximo va primero"). Un default(DateTime)
            // es el año 1 y se colaría a la cabeza de la lista para siempre.
            _logger.LogWarning(
                "UmbracoEventCatalogSource: eventPage slug='{Slug}' sin eventStart; se omite.", slug);
            return null;
        }

        return new EventSummary(
            // El slug ES el id, igual que el SKU lo es en Tienda: GetEventAsync ya casa por
            // cualquiera de los dos, así que emitirlos iguales no rompe nada y evita inventar
            // un segundo identificador que nadie autora.
            Id: slug,
            Slug: slug,
            Title: title,
            Category: node.Value<string>("eventCategory")?.Trim() ?? string.Empty,
            City: node.Value<string>("eventCity")?.Trim() ?? string.Empty,
            Venue: node.Value<string>("eventVenue")?.Trim() ?? string.Empty,
            StartUtc: new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), ColombiaOffset),
            // El lector de MediaPicker3 vive en MediaPickerReader y es el ÚNICO: el picker
            // está configurado single, así que pedirlo como colección devuelve null sin
            // lanzar — el bug que dejó la tienda entera sin fotos.
            ImageUrl: MediaPickerReader.ReadFirstMediaUrl(node, "eventImage") ?? string.Empty,
            PriceFrom: priceFrom,
            Currency: Currency,
            Mode: NormalizeMode(node.Value<string>("eventMode")),
            Geo: ReadGeo(node, slug));
    }

    /// <summary>
    /// El precio "desde", o false si el texto no es INEQUÍVOCAMENTE un precio. <b>Solo dígitos.</b>
    /// Vacío es legítimo y vale 0 (evento gratuito o sin precio publicado aún).
    /// </summary>
    /// <remarks>
    /// <b>Misma trampa que costó dinero en Tienda, misma regla.</b> <c>eventPriceFrom</c> es
    /// <c>Umbraco.TextBox</c>, así que el editor teclea texto libre — y <c>"180.000"</c> SÍ
    /// parsea en InvariantCulture, porque ahí el punto es separador DECIMAL: da <b>180</b>. El
    /// editor escribe 180 mil pesos y la agenda anuncia "desde $180". No es un fallo de parseo
    /// sino un precio plausible equivocado por 1000×, y ninguna guarda de "&gt; 0" lo ve.
    ///
    /// <para><b>Por qué se OMITE el evento en vez de emitirlo en 0</b>, que es una decisión y no
    /// un calco automático: aquí el precio es de exhibición ("desde $X") y el cobro real sale
    /// de los tiers, así que la tentación es servirlo igual. Pero un 0 se pinta como
    /// <i>Gratis</i>, y un evento pago anunciado como gratuito es exactamente la basura que
    /// esta fuente no debe emitir. Que falte una ficha se ve al instante y el editor la
    /// arregla; un "Gratis" falso lo descubre el asistente en la puerta.</para>
    /// </remarks>
    private bool TryParsePriceFrom(IPublishedContent node, string slug, out decimal priceFrom)
    {
        priceFrom = 0m;
        var raw = node.Value<string>("eventPriceFrom")?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            // No es un error: el campo es opcional y 0 significa "sin precio publicado".
            return true;
        }

        // Solo dígitos: ni "180.000" (que parsearía a 180) ni "$180000" ni "180,000".
        if (raw.All(char.IsAsciiDigit)
            && decimal.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out priceFrom))
        {
            return true;
        }

        _logger.LogError(
            "UmbracoEventCatalogSource: evento '{Slug}' tiene eventPriceFrom='{Raw}', que no es un precio " +
            "inequívoco. Se OMITE del catálogo. Formato esperado: SOLO DÍGITOS, sin puntos ni símbolos " +
            "(ej. 180000, no \"180.000\" — eso se leería como 180 pesos).",
            slug, raw);
        priceFrom = 0m;
        return false;
    }

    /// <summary>
    /// El modo de venta normalizado: <c>general</c> (cupo por cantidad) o <c>reserved</c>
    /// (asiento numerado). Cualquier otra cosa cae a <c>general</c>.
    /// </summary>
    /// <remarks>
    /// <b>Cae a general y no se rechaza porque el modo elige PANTALLA, no precio.</b> Un modo
    /// desconocido con seat-map ausente dejaría la ficha sin forma de seleccionar; general es
    /// el modo que siempre funciona. Se loguea para que la errata se vea.
    /// </remarks>
    private string NormalizeMode(string? raw)
    {
        var mode = raw?.Trim();
        if (string.IsNullOrEmpty(mode))
        {
            return "general";
        }

        if (string.Equals(mode, "general", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "reserved", StringComparison.OrdinalIgnoreCase))
        {
            return mode.ToLowerInvariant();
        }

        _logger.LogWarning(
            "UmbracoEventCatalogSource: eventMode='{Raw}' no reconocido; se trata como 'general'. " +
            "Valores válidos: general | reserved.", raw);
        return "general";
    }

    /// <summary>
    /// La coordenada del recinto, o null si no está completa o no es plausible.
    /// </summary>
    /// <remarks>
    /// <b>Se exigen las DOS y dentro de rango.</b> Media coordenada no es medio pin: es un pin
    /// en el meridiano de Greenwich. Y aquí el punto SÍ es separador decimal (4.6584 es una
    /// latitud, no cuatro mil), así que la regla de "solo dígitos" del precio no aplica —
    /// lo que protege es el rango: una longitud tecleada en el campo de latitud sale de
    /// ±90 y se descarta en vez de mandar el mapa al océano.
    /// </remarks>
    private EventGeo? ReadGeo(IPublishedContent node, string slug)
    {
        var rawLat = node.Value<string>("eventLat")?.Trim();
        var rawLng = node.Value<string>("eventLng")?.Trim();

        if (string.IsNullOrEmpty(rawLat) && string.IsNullOrEmpty(rawLng))
        {
            // Sin geo el evento sigue siendo válido: simplemente no sale en el mapa.
            return null;
        }

        var latOk = double.TryParse(rawLat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            && lat is >= -90d and <= 90d;
        var lngOk = double.TryParse(rawLng, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
            && lng is >= -180d and <= 180d;

        if (latOk && lngOk)
        {
            return new EventGeo(lat, lng);
        }

        _logger.LogWarning(
            "UmbracoEventCatalogSource: evento '{Slug}' tiene geo incompleta o fuera de rango " +
            "(eventLat='{Lat}', eventLng='{Lng}'); no saldrá en el mapa. Formato: grados decimales " +
            "con punto (ej. 4.6584 / -74.0936).",
            slug, rawLat, rawLng);
        return null;
    }

    // ── Ficha comprable ──────────────────────────────────────────────────────

    /// <summary>
    /// <c>eventPage</c> → <see cref="EventDetail"/>. Devuelve null exactamente cuando
    /// <see cref="Project"/> devolvería null: la ficha y el resumen se omiten juntos.
    /// </summary>
    /// <remarks>
    /// <b>Este método LEE; no decide.</b> Las reglas de qué localidad se vende, qué zona se
    /// descarta y cómo se generan los asientos viven en <see cref="EventContentRules"/>, que es
    /// pura y por eso testeable. Aquí solo se traduce contenido de Umbraco a los records planos
    /// que esas reglas consumen, y se emiten al log los problemas que devuelven.
    /// </remarks>
    private EventDetail? ProjectDetail(IPublishedContent node)
    {
        var summary = Project(node);
        if (summary is null)
        {
            return null;
        }

        var slug = summary.Slug;

        var tiers = Report(EventContentRules.BuildTiers(slug, ReadTierDrafts(node), summary.Currency));

        var venueName = node.Value<string>("eventVenueMapName")?.Trim();
        if (string.IsNullOrEmpty(venueName))
        {
            // Cae al recinto del resumen antes que a una cadena vacía: el mapa siempre tiene
            // encabezado, y el venue es el nombre que el asistente ya vio en la tarjeta.
            venueName = summary.Venue;
        }

        var seatMap = Report(EventContentRules.BuildSeatMap(
            slug, ReadZoneDrafts(node), tiers, summary.Currency, venueName));

        var mode = Report(EventContentRules.ResolveMode(slug, summary.Mode, seatMap is not null));
        if (!string.Equals(mode, summary.Mode, StringComparison.Ordinal))
        {
            summary = summary with { Mode = mode };
        }

        return new EventDetail(
            Summary: summary,
            Description: node.Value<string>("eventDescription")?.Trim() ?? string.Empty,
            Organizer: node.Value<string>("eventOrganizer")?.Trim() ?? string.Empty,
            Tiers: tiers,
            SeatMap: seatMap,
            Artist: EventContentRules.BuildArtist(
                node.Value<string>("eventArtistName"),
                node.Value<string>("eventArtistHeadline"),
                node.Value<int>("eventArtistFollowers")),
            Highlights: EventContentRules.CleanTextList(ReadTextList(node, "eventHighlights")),
            Sessions: Report(EventContentRules.BuildSessions(slug, ReadSessionDrafts(node))));
    }

    /// <summary>
    /// Emite al log los problemas que devolvieron las reglas y entrega lo proyectado.
    /// </summary>
    private T Report<T>(EventContentResult<T> result)
    {
        foreach (var issue in result.Issues)
        {
            if (issue.Level == EventContentIssueLevel.Error)
            {
                _logger.LogError("UmbracoEventCatalogSource: {Issue}", issue.Message);
            }
            else
            {
                _logger.LogWarning("UmbracoEventCatalogSource: {Issue}", issue.Message);
            }
        }

        return result.Value;
    }

    private static IReadOnlyList<EventTierContent> ReadTierDrafts(IPublishedContent node)
        => ReadBlocks(node, "eventTiers")
            .Select(b => new EventTierContent(
                Code: b.Value<string>("tierCode"),
                Name: b.Value<string>("tierName"),
                Price: b.Value<int>("tierPrice"),
                Capacity: b.Value<int>("tierCapacity"),
                MaxPerOrder: b.Value<int>("tierMaxPerOrder"),
                ZoneId: b.Value<string>("tierZoneId"),
                Description: b.Value<string>("tierDescription"),
                Perks: ReadTextList(b, "tierPerks"),
                SaleWindow: b.Value<string>("tierSaleWindow"),
                Featured: b.Value<bool>("tierFeatured")))
            .ToList();

    private static IReadOnlyList<EventZoneContent> ReadZoneDrafts(IPublishedContent node)
        => ReadBlocks(node, "eventZones")
            .Select(b => new EventZoneContent(
                Id: b.Value<string>("zoneId"),
                Name: b.Value<string>("zoneName"),
                TierCode: b.Value<string>("zoneTierCode"),
                Price: b.Value<int>("zonePrice"),
                RowLabels: ReadTextList(b, "zoneRowLabels"),
                SeatsPerRow: b.Value<int>("zoneSeatsPerRow")))
            .ToList();

    private static IReadOnlyList<EventSessionContent> ReadSessionDrafts(IPublishedContent node)
        => ReadBlocks(node, "eventSessions")
            .Select(b => new EventSessionContent(
                Time: b.Value<string>("sessionTime"),
                Title: b.Value<string>("sessionTitle"),
                Speaker: b.Value<string>("sessionSpeaker")))
            .ToList();


    /// <summary>
    /// Los bloques de una propiedad BlockList, o vacío si la propiedad no está poblada.
    /// </summary>
    /// <remarks>
    /// Un BlockList vacío llega como null, no como colección vacía, y ese null es la diferencia
    /// entre "el editor no llenó localidades" y una <c>NullReferenceException</c> en cada
    /// request de la ficha.
    /// </remarks>
    private static IReadOnlyList<IPublishedElement> ReadBlocks(IPublishedElement node, string alias)
    {
        var blocks = node.Value<BlockListModel>(alias);
        if (blocks is null || blocks.Count == 0)
        {
            return Array.Empty<IPublishedElement>();
        }

        return blocks.Select(b => b.Content).ToList();
    }

    /// <summary>
    /// Los renglones no vacíos de un campo de texto repetible, o vacío si no hay ninguno.
    /// </summary>
    /// <remarks>
    /// Se recortan y se descartan los blancos porque un Enter de más en el backoffice se
    /// convierte, si no, en una viñeta vacía en la tarjeta.
    /// </remarks>
    private static IReadOnlyList<string> ReadTextList(IPublishedElement node, string alias)
    {
        var raw = node.Value<IEnumerable<string>>(alias);
        if (raw is null)
        {
            return Array.Empty<string>();
        }

        return raw
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();
    }
}
