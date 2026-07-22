using System.Globalization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
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
/// <para><b>Esta fuente emite el RESUMEN, no la ficha.</b> <see cref="EventSummary"/> es lo que
/// el catálogo lista; los tiers y el seat-map (<c>EventDetail</c>) NO están modelados en
/// <c>eventPage</c> todavía y siguen saliendo del stub. Es la frontera deliberada de esta
/// rebanada, no un olvido: modelar aforo y precio por tier como contenido es su propia
/// decisión, y sembrarla a medias dejaría fichas sin nada que comprar.</para>
/// </remarks>
public sealed class UmbracoEventCatalogSource : ICatalogSource<EventSummary>
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
    public Task<IReadOnlyList<EventSummary>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            // Fuera de un contexto de Umbraco no hay contenido que servir. Agenda vacía y no
            // una excepción: la pantalla se degrada, no revienta.
            _logger.LogWarning("UmbracoEventCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Task.FromResult<IReadOnlyList<EventSummary>>(Array.Empty<EventSummary>());
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
            return Task.FromResult<IReadOnlyList<EventSummary>>(Array.Empty<EventSummary>());
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoEventCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Task.FromResult<IReadOnlyList<EventSummary>>(Array.Empty<EventSummary>());
        }

        var candidates = siteRoot.DescendantsOfType(EventPageAlias).ToList();

        var events = candidates
            .Select(Project)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        var skipped = candidates.Count - events.Count;
        if (skipped > 0)
        {
            // El conteo va en UNA línea al final y no solo por nodo: "se omitieron 7 de 24"
            // es lo que hace ver que la siembra está mal; siete advertencias sueltas entre
            // el ruido del log, no.
            _logger.LogWarning(
                "UmbracoEventCatalogSource: se omitieron {Skipped} de {Total} eventPage por datos incompletos.",
                skipped, candidates.Count);
        }

        return Task.FromResult<IReadOnlyList<EventSummary>>(events);
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
}
