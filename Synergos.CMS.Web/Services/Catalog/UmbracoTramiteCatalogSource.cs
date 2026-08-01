using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// El catálogo de trámites respaldado por el CONTENIDO del CMS: sirve los
/// <c>tramitePage</c> que el editor de la entidad autoró, en vez del seed hardcodeado.
/// </summary>
/// <remarks>
/// <b>Quinto vertical con esta forma</b> (Tienda, Eventos, Inmobiliaria, Booking y ahora
/// Gobierno). Gobierno era uno de los dos últimos con catálogo y CERO superficie CMS: los siete
/// trámites vivían sembrados en C# dentro de <c>StubTramiteCatalogProvider</c>, así que un
/// funcionario de una entidad pública no podía publicar el octavo.
///
/// <para>Es la ÚNICA clase de Gobierno que toca Umbraco. Las reglas viven en
/// <see cref="TramiteContentRules"/>, que es pura; el proveedor y el controller siguen sin saber
/// que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Gov = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redespliegue.</para>
/// </remarks>
public sealed class UmbracoTramiteCatalogSource : ICatalogSource<TramiteDetail>
{
    internal const string Vertical = "Gov";
    private const string TramitePageAlias = "tramitePage";
    private const string SiteRootAlias = "siteRoot";

    /// <summary>
    /// Moneda del portal. Constante y no una propiedad de schema, por lo mismo que en Eventos e
    /// Inmobiliaria: un deploy es un origen, y dejar que el editor la escriba garantiza
    /// "cop"/"COP "/"pesos" en el mismo catálogo. Aquí además una tasa es un cobro público en
    /// pesos: no hay trámite colombiano tarifado en otra moneda.
    /// </summary>
    private const string Currency = "COP";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ICultureDictionaryFactory _dictionaryFactory;
    private readonly ILogger<UmbracoTramiteCatalogSource> _logger;

    public UmbracoTramiteCatalogSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ICultureDictionaryFactory dictionaryFactory,
        ILogger<UmbracoTramiteCatalogSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _dictionaryFactory = dictionaryFactory;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las otras cuatro fuentes: el scope de este catálogo no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Gov</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    public Task<IReadOnlyList<TramiteDetail>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        if (nodes.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<TramiteDetail>>(Array.Empty<TramiteDetail>());
        }

        // Los pasos se resuelven UNA vez por llamada, no uno por trámite: son los mismos tres
        // para todo el catálogo y cada lectura del diccionario es una búsqueda.
        var stepLabels = ResolveStepLabels();

        var tramites = new List<TramiteDetail>(nodes.Count);
        foreach (var node in nodes)
        {
            var result = TramiteContentRules.BuildTramite(Read(node), Currency, stepLabels);

            foreach (var issue in result.Issues)
            {
                if (issue.Level == EventContentIssueLevel.Error)
                {
                    _logger.LogError("UmbracoTramiteCatalogSource: {Issue}", issue.Message);
                }
                else
                {
                    _logger.LogWarning("UmbracoTramiteCatalogSource: {Issue}", issue.Message);
                }
            }

            if (result.Value is not null)
            {
                tramites.Add(result.Value);
            }
        }

        var skipped = nodes.Count - tramites.Count;
        if (skipped > 0)
        {
            // El conteo va en UNA línea al final: "se omitieron 7 de 24" es lo que hace ver que
            // la siembra está mal; siete advertencias sueltas entre el ruido del log, no.
            _logger.LogWarning(
                "UmbracoTramiteCatalogSource: se omitieron {Skipped} de {Total} tramitePage por datos incompletos.",
                skipped, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<TramiteDetail>>(tramites);
    }

    /// <summary>
    /// Los <c>tramitePage</c> publicados bajo el siteRoot configurado.
    /// </summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            // Fuera de un contexto de Umbraco no hay contenido que servir. Catálogo vacío y no
            // una excepción: la pantalla se degrada, no revienta.
            _logger.LogWarning("UmbracoTramiteCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // SIN scope NO se sirve nada. Fallar cerrado y ruidoso es lo correcto para un error
            // de configuración: servir sin acotar mezclaría los trámites de todos los siteRoots
            // —los de otra alcaldía, con otras tasas y otros requisitos— en silencio.
            _logger.LogError(
                "UmbracoTramiteCatalogSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoTramiteCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(TramitePageAlias).ToList();
    }

    /// <summary>
    /// Los textos de los tres pasos, del Dictionary uSync con respaldo en es-CO.
    /// </summary>
    /// <remarks>
    /// <b>Son las ÚNICAS cadenas que esta rebanada lleva al Dictionary</b>, y por la regla del
    /// ADR 0118 §3: una cadena va al Dictionary uSync si y solo si el SERVIDOR la emite.
    /// <c>TramiteStep.Title</c> y <c>TramiteStep.Detail</c> viajan ya escritos en el JSON de la
    /// ficha; todo el resto del copy del vertical vive en la i18n del bundle Angular y una clave
    /// para eso nacería huérfana.
    ///
    /// <para>El respaldo NO es defensa de más: el Dictionary se aplica con un Import manual, así
    /// que entre el despliegue del código y ese Import las claves no existen todavía.</para>
    /// </remarks>
    private TramiteStepLabels ResolveStepLabels()
    {
        var dictionary = _dictionaryFactory.CreateDictionary();

        return new TramiteStepLabels(
            SubmitTitle: Translate(dictionary, "Gov.Step.Submit.Title", "Radicar la solicitud"),
            SubmitDetail: Translate(dictionary, "Gov.Step.Submit.Detail",
                "Diligencie el formulario y adjunte los documentos requeridos."),
            ReviewTitle: Translate(dictionary, "Gov.Step.Review.Title", "Revisión de la entidad"),
            ReviewDetail: Translate(dictionary, "Gov.Step.Review.Detail",
                "Un funcionario valida sus datos y documentos."),
            ResolutionTitle: Translate(dictionary, "Gov.Step.Resolution.Title", "Resolución"),
            ResolutionDetail: Translate(dictionary, "Gov.Step.Resolution.Detail",
                "Recibe la respuesta y el documento resultante en su carpeta."));
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
    /// <c>tramitePage</c> → el record plano que consumen las reglas. Solo LEE.
    /// </summary>
    private static TramitePageContent Read(IPublishedContent node) => new(
        Slug: node.Value<string>("tramiteSlug"),
        Name: node.Value<string>("tramiteName"),
        Summary: node.Value<string>("tramiteSummary"),
        Category: node.Value<string>("tramiteCategory"),
        Agency: node.Value<string>("tramiteAgency"),
        Channel: node.Value<string>("tramiteChannel"),
        Description: node.Value<string>("tramiteDescription"),
        Eligibility: ReadTextList(node, "tramiteEligibility"),
        Required: ReadTextList(node, "tramiteRequired"),
        Normativa: node.Value<string>("tramiteNormativa"),
        IsFree: node.Value<bool>("tramiteIsFree"),
        Fee: node.Value<decimal>("tramiteFee"),
        EstimatedDays: node.Value<int>("tramiteEstimatedDays"),
        RequiresAppointment: node.Value<bool>("tramiteRequiresAppointment"),
        Sections: ReadSections(node));

    /// <summary>
    /// Las secciones del formulario y sus preguntas, en el orden en que el editor las puso.
    /// </summary>
    private static IReadOnlyList<TramiteSectionContent> ReadSections(IPublishedContent node)
        => ReadBlocks(node, "tramiteFormSections")
            .Select(section => new TramiteSectionContent(
                Id: section.Value<string>("tramiteSectionId"),
                Title: section.Value<string>("tramiteSectionTitle"),
                Fields: ReadFields(section)))
            .ToList();

    private static IReadOnlyList<TramiteFieldContent> ReadFields(IPublishedElement section)
        => ReadBlocks(section, "tramiteSectionFields")
            .Select(field => new TramiteFieldContent(
                Id: field.Value<string>("tramiteFieldId"),
                Label: field.Value<string>("tramiteFieldLabel"),
                Type: field.Value<string>("tramiteFieldType"),
                Required: field.Value<bool>("tramiteFieldRequired"),
                Help: field.Value<string>("tramiteFieldHelp"),
                Options: ReadTextList(field, "tramiteFieldOptions"),
                Pattern: field.Value<string>("tramiteFieldPattern")))
            .ToList();

    /// <summary>
    /// Los bloques de una propiedad BlockList, o vacío si la propiedad no está poblada.
    /// </summary>
    /// <remarks>
    /// Un BlockList vacío llega como null, no como colección vacía, y ese null es la diferencia
    /// entre "el editor no llenó el formulario" y una <c>NullReferenceException</c> en cada
    /// request del catálogo.
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

    private static IReadOnlyList<string> ReadTextList(IPublishedElement node, string alias)
        => node.Value<IEnumerable<string>>(alias)?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
}
