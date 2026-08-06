using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// La fuente del portal inmobiliario respaldada por el CONTENIDO del CMS: sirve los
/// <c>propertyListing</c> que el editor autoró, en vez del seed hardcodeado.
/// </summary>
/// <remarks>
/// <b>Tercer vertical con esta forma</b> (Tienda, Eventos y ahora Inmobiliaria), y el primero
/// que nace con su DocType: hasta esta rebanada Propiedades no tenía NINGUNA superficie CMS —
/// el catálogo entero vivía sembrado en C#, así que un editor no podía publicar un inmueble.
///
/// <para>Es la ÚNICA clase de Inmobiliaria que toca Umbraco. Las reglas viven en
/// <see cref="PropertyContentRules"/>, que es pura; el motor y el descriptor siguen en
/// Application, sin saber que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Realty = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redespliegue.</para>
///
/// <para><b>Emite la ficha completa, no el resumen.</b> A diferencia de la primera rebanada de
/// Eventos, aquí no hay frontera parcial: <see cref="PropertyDetail"/> lleva su propio
/// <see cref="PropertyDetail.Summary"/> dentro, así que servir media cosa habría sido
/// gratuito y confuso.</para>
/// </remarks>
public sealed class UmbracoPropertyCatalogSource : ICatalogSource<PropertyDetail>
{
    internal const string Vertical = "Realty";
    private const string PropertyListingAlias = "propertyListing";
    private const string SiteRootAlias = "siteRoot";

    /// <summary>
    /// Moneda del portal. Constante y no una propiedad de schema, por la misma razón que en
    /// Eventos: un deploy es un origen, y dejar que el editor la escriba garantiza
    /// "cop"/"COP "/"pesos" en el mismo catálogo.
    /// </summary>
    private const string Currency = "COP";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ICultureDictionaryFactory _dictionaryFactory;
    private readonly ILogger<UmbracoPropertyCatalogSource> _logger;

    public UmbracoPropertyCatalogSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ICultureDictionaryFactory dictionaryFactory,
        ILogger<UmbracoPropertyCatalogSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _dictionaryFactory = dictionaryFactory;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las otras dos fuentes: el scope de este catálogo no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Realty</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    public Task<IReadOnlyList<PropertyDetail>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        if (nodes.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<PropertyDetail>>(Array.Empty<PropertyDetail>());
        }

        // Las etiquetas se resuelven UNA vez por llamada, no una por inmueble: son las mismas
        // siete para todo el catálogo y cada lectura del diccionario es una búsqueda.
        var labels = ResolveSpecLabels();

        var listings = new List<PropertyDetail>(nodes.Count);
        foreach (var node in nodes)
        {
            var result = PropertyContentRules.BuildListing(Read(node), Currency, labels);

            foreach (var issue in result.Issues)
            {
                if (issue.Level == EventContentIssueLevel.Error)
                {
                    _logger.LogError("UmbracoPropertyCatalogSource: {Issue}", issue.Message);
                }
                else
                {
                    _logger.LogWarning("UmbracoPropertyCatalogSource: {Issue}", issue.Message);
                }
            }

            if (result.Value is not null)
            {
                listings.Add(result.Value);
            }
        }

        var skipped = nodes.Count - listings.Count;
        if (skipped > 0)
        {
            // El conteo va en UNA línea al final: "se omitieron 7 de 24" es lo que hace ver
            // que la siembra está mal; siete advertencias sueltas entre el ruido del log, no.
            _logger.LogWarning(
                "UmbracoPropertyCatalogSource: se omitieron {Skipped} de {Total} propertyListing por datos incompletos.",
                skipped, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<PropertyDetail>>(listings);
    }

    /// <summary>
    /// Los <c>propertyListing</c> publicados bajo el siteRoot configurado.
    /// </summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            // Fuera de un contexto de Umbraco no hay contenido que servir. Portal vacío y no
            // una excepción: la pantalla se degrada, no revienta.
            _logger.LogWarning("UmbracoPropertyCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // SIN scope NO se sirve nada. Fallar cerrado y ruidoso es lo correcto para un
            // error de configuración: servir sin acotar mezclaría el inventario de todos los
            // siteRoots, en silencio.
            _logger.LogError(
                "UmbracoPropertyCatalogSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoPropertyCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(PropertyListingAlias).ToList();
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
    private PropertySpecLabels ResolveSpecLabels()
    {
        var dictionary = _dictionaryFactory.CreateDictionary();

        return new PropertySpecLabels(
            Area: Translate(dictionary, "Realty.Spec.Area", "Área"),
            Beds: Translate(dictionary, "Realty.Spec.Beds", "Habitaciones"),
            Baths: Translate(dictionary, "Realty.Spec.Baths", "Baños"),
            Parking: Translate(dictionary, "Realty.Spec.Parking", "Parqueaderos"),
            Stratum: Translate(dictionary, "Realty.Spec.Stratum", "Estrato"),
            Age: Translate(dictionary, "Realty.Spec.Age", "Antigüedad"),
            Floor: Translate(dictionary, "Realty.Spec.Floor", "Piso"));
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
    /// <c>propertyListing</c> → el record plano que consumen las reglas. Solo LEE.
    /// </summary>
    private static PropertyListingContent Read(IPublishedContent node) => new(
        Slug: node.Value<string>("propertySlug"),
        Title: node.Value<string>("propertyTitle"),
        Operation: node.Value<string>("propertyOperation"),
        Type: node.Value<string>("propertyType"),
        Price: node.Value<decimal>("propertyPrice"),
        Featured: node.Value<bool>("propertyFeatured"),
        Description: node.Value<string>("propertyDescription"),
        Gallery: ReadGallery(node, "propertyGallery"),
        Amenities: ReadTextList(node, "propertyAmenities"),
        Beds: node.Value<int>("propertyBeds"),
        Baths: node.Value<int>("propertyBaths"),
        AreaM2: node.Value<int>("propertyAreaM2"),
        Parking: node.Value<int>("propertyParking"),
        Stratum: node.Value<int>("propertyStratum"),
        Age: node.Value<string>("propertyAge"),
        Floor: node.Value<string>("propertyFloor"),
        City: node.Value<string>("propertyCity"),
        Neighborhood: node.Value<string>("propertyNeighborhood"),
        Address: node.Value<string>("propertyAddress"),
        Lat: ReadCoordinate(node, "propertyLat"),
        Lng: ReadCoordinate(node, "propertyLng"),
        AgentName: node.Value<string>("propertyAgentName"),
        AgentPhone: node.Value<string>("propertyAgentPhone"));

    /// <summary>
    /// Las URLs de la galería, en el orden en que el editor las puso.
    /// </summary>
    /// <remarks>
    /// El picker está configurado MÚLTIPLE, así que se pide como colección. Pedirlo en singular
    /// devolvería solo la primera y la ficha se quedaría con una foto — el reverso
    /// exacto del bug que dejó la tienda entera sin fotos por pedir en plural un picker
    /// configurado en singular.
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
    /// Una coordenada del picker decimal, o null si el editor la dejó vacía.
    /// </summary>
    /// <remarks>
    /// <c>Umbraco.Decimal</c> devuelve <c>0</c> tanto para "vacío" como para el cero real, y
    /// aquí la diferencia importa: (0, 0) es el contrato de "sin pin" que el módulo Angular
    /// ya respeta, mientras que un 0 en UNA sola coordenada es media coordenada y hay que
    /// avisarlo. Convertir el 0 en null deja que las reglas apliquen esa distinción.
    /// </remarks>
    private static double? ReadCoordinate(IPublishedContent node, string alias)
    {
        var value = node.Value<decimal>(alias);
        return value == 0m ? null : (double)value;
    }
}
