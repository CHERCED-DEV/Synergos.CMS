using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// El inventario de estadías respaldado por el CONTENIDO del CMS: sirve los
/// <c>stayListing</c> que el hotelero autoró, en vez del seed hardcodeado.
/// </summary>
/// <remarks>
/// <b>Cuarto vertical con esta forma</b> (Tienda, Eventos, Inmobiliaria y ahora Booking).
/// Booking era el último con catálogo y CERO superficie CMS: las cuatro estadías vivían
/// sembradas en C# dentro de <c>StubStayContentProvider</c>, así que un hotelero no podía
/// publicar la quinta.
///
/// <para>Es la ÚNICA clase de Booking que toca Umbraco. Las reglas viven en
/// <see cref="StayContentRules"/>, que es pura; el proveedor y el controller siguen sin saber
/// que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Booking = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redespliegue.</para>
/// </remarks>
public sealed class UmbracoStayContentSource : ICatalogSource<StayDetail>
{
    internal const string Vertical = "Booking";
    private const string StayListingAlias = "stayListing";
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ICultureDictionaryFactory _dictionaryFactory;
    private readonly ILogger<UmbracoStayContentSource> _logger;

    public UmbracoStayContentSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ICultureDictionaryFactory dictionaryFactory,
        ILogger<UmbracoStayContentSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _dictionaryFactory = dictionaryFactory;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las otras tres fuentes: el scope de este catálogo no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Booking</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    public Task<IReadOnlyList<StayDetail>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        if (nodes.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<StayDetail>>(Array.Empty<StayDetail>());
        }

        // Las etiquetas se resuelven UNA vez por llamada, no una por estadía: son las mismas
        // nueve para todo el catálogo y cada lectura del diccionario es una búsqueda.
        var specLabels = ResolveSpecLabels();
        var reviewLabels = ResolveReviewLabels();

        var stays = new List<StayDetail>(nodes.Count);
        foreach (var node in nodes)
        {
            var result = StayContentRules.BuildStay(Read(node), specLabels, reviewLabels);
            Emit(result.Issues);

            if (result.Value is not null)
            {
                stays.Add(result.Value);
            }
        }

        // Los códigos duplicados solo se ven con el catálogo entero en la mano, así que la
        // comprobación va acá y no dentro de la proyección de cada nodo.
        Emit(StayContentRules.FindRoomTypeCollisions(stays));

        var skipped = nodes.Count - stays.Count;
        if (skipped > 0)
        {
            // El conteo va en UNA línea al final: "se omitieron 7 de 24" es lo que hace ver que
            // la siembra está mal; siete advertencias sueltas entre el ruido del log, no.
            _logger.LogWarning(
                "UmbracoStayContentSource: se omitieron {Skipped} de {Total} stayListing por datos incompletos.",
                skipped, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<StayDetail>>(stays);
    }

    private void Emit(IReadOnlyList<EventContentIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Level == EventContentIssueLevel.Error)
            {
                _logger.LogError("UmbracoStayContentSource: {Issue}", issue.Message);
            }
            else
            {
                _logger.LogWarning("UmbracoStayContentSource: {Issue}", issue.Message);
            }
        }
    }

    /// <summary>
    /// Los <c>stayListing</c> publicados bajo el siteRoot configurado.
    /// </summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            // Fuera de un contexto de Umbraco no hay contenido que servir. Catálogo vacío y no
            // una excepción: la pantalla se degrada, no revienta.
            _logger.LogWarning("UmbracoStayContentSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // SIN scope NO se sirve nada. Fallar cerrado y ruidoso es lo correcto para un error
            // de configuración: servir sin acotar mezclaría el inventario de todos los
            // siteRoots, en silencio.
            _logger.LogError(
                "UmbracoStayContentSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoStayContentSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(StayListingAlias).ToList();
    }

    /// <summary>
    /// Las etiquetas de las características, del Dictionary uSync con respaldo en es-CO.
    /// </summary>
    /// <remarks>
    /// El respaldo NO es defensa de más: el Dictionary se aplica con un Import manual, así que
    /// entre el despliegue del código y ese Import las claves no existen todavía. Sin respaldo
    /// la ficha saldría con etiquetas en blanco —o con la clave cruda, según el proveedor— en
    /// esa ventana.
    /// </remarks>
    private StaySpecLabels ResolveSpecLabels()
    {
        var dictionary = _dictionaryFactory.CreateDictionary();

        return new StaySpecLabels(
            Rooms: Translate(dictionary, "Stay.Spec.Rooms", "Habitaciones"),
            MaxGuests: Translate(dictionary, "Stay.Spec.MaxGuests", "Huéspedes máx. por habitación"),
            RoomSize: Translate(dictionary, "Stay.Spec.RoomSize", "Tamaño promedio"),
            CheckIn: Translate(dictionary, "Stay.Spec.CheckIn", "Check-in"),
            CheckOut: Translate(dictionary, "Stay.Spec.CheckOut", "Check-out"));
    }

    /// <summary>
    /// Los nombres de los cuatro ejes de reseñas, del Dictionary uSync con respaldo en es-CO.
    /// </summary>
    private StayReviewLabels ResolveReviewLabels()
    {
        var dictionary = _dictionaryFactory.CreateDictionary();

        return new StayReviewLabels(
            Cleanliness: Translate(dictionary, "Stay.Review.Cleanliness", "Limpieza"),
            Location: Translate(dictionary, "Stay.Review.Location", "Ubicación"),
            Service: Translate(dictionary, "Stay.Review.Service", "Servicio"),
            Value: Translate(dictionary, "Stay.Review.Value", "Relación precio-calidad"));
    }

    private static string Translate(ICultureDictionary dictionary, string key, string fallback)
    {
        var value = dictionary[key];
        // Umbraco devuelve cadena vacía cuando la clave no existe, y en algunos proveedores
        // devuelve la clave misma. Ninguna de las dos es una etiqueta.
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal)
            ? fallback
            : value;
    }

    /// <summary>
    /// <c>stayListing</c> → el record plano que consumen las reglas. Solo LEE.
    /// </summary>
    private static StayListingContent Read(IPublishedContent node) => new(
        Slug: node.Value<string>("staySlug"),
        Name: node.Value<string>("stayName"),
        City: node.Value<string>("stayCity"),
        Region: node.Value<string>("stayRegion"),
        Description: node.Value<string>("stayDescription"),
        Gallery: ReadGallery(node, "stayGallery"),
        Amenities: ReadTextList(node, "stayAmenities"),
        RoomTypeCodes: ReadTextList(node, "stayRoomTypeCodes"),
        Rooms: node.Value<int>("stayRooms"),
        MaxGuests: node.Value<int>("stayMaxGuests"),
        RoomSizeM2: node.Value<int>("stayRoomSizeM2"),
        CheckIn: ReadTime(node, "stayCheckIn"),
        CheckOut: ReadTime(node, "stayCheckOut"),
        Address: node.Value<string>("stayAddress"),
        Lat: ReadCoordinate(node, "stayLat"),
        Lng: ReadCoordinate(node, "stayLng"),
        ReviewAverage: node.Value<decimal>("stayReviewAverage"),
        ReviewCount: node.Value<int>("stayReviewCount"),
        ReviewHighlight: node.Value<string>("stayReviewHighlight"),
        ScoreCleanliness: node.Value<decimal>("stayScoreCleanliness"),
        ScoreLocation: node.Value<decimal>("stayScoreLocation"),
        ScoreService: node.Value<decimal>("stayScoreService"),
        ScoreValue: node.Value<decimal>("stayScoreValue"));

    /// <summary>
    /// Las URLs de la galería, en el orden en que el hotelero las puso.
    /// </summary>
    /// <remarks>
    /// El picker está configurado MÚLTIPLE, así que se pide como colección. Pedirlo en singular
    /// devolvería solo la primera y la ficha se quedaría con una foto.
    /// </remarks>
    private static IReadOnlyList<string> ReadGallery(IPublishedContent node, string alias)
    {
        var media = node.Value<IEnumerable<IPublishedContent>>(alias);
        if (media is null)
        {
            return Array.Empty<string>();
        }

        return media
            .Select(m => m?.Url() ?? string.Empty)
            .Where(u => u.Length > 0)
            .ToList();
    }

    private static IReadOnlyList<string> ReadTextList(IPublishedContent node, string alias)
        => node.Value<IEnumerable<string>>(alias)?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>
    /// La hora del selector, o null si el hotelero la dejó vacía.
    /// </summary>
    /// <remarks>
    /// El editor es <c>Umbraco.DateTime</c> con formato <c>HH:mm</c>, así que guarda un
    /// <c>DateTime</c> completo del que aquí solo importa la hora. Un campo vacío devuelve
    /// <c>default(DateTime)</c> —año 1—, y esa es la forma de distinguir "sin autorar" de
    /// "medianoche": sin la distinción, toda propiedad sin check-in declararía entrada a las
    /// 12:00 a. m.
    /// </remarks>
    private static TimeOnly? ReadTime(IPublishedContent node, string alias)
    {
        var value = node.Value<DateTime>(alias);
        return value == default ? null : TimeOnly.FromDateTime(value);
    }

    /// <summary>
    /// Una coordenada del picker decimal, o null si el hotelero la dejó vacía.
    /// </summary>
    /// <remarks>
    /// <c>Umbraco.Decimal</c> devuelve <c>0</c> tanto para "vacío" como para el cero real, y
    /// aquí la diferencia importa: (0, 0) es el contrato de "sin pin", mientras que un 0 en UNA
    /// sola coordenada es media coordenada y hay que avisarlo.
    /// </remarks>
    private static double? ReadCoordinate(IPublishedContent node, string alias)
    {
        var value = node.Value<decimal>(alias);
        return value == 0m ? null : (double)value;
    }
}
