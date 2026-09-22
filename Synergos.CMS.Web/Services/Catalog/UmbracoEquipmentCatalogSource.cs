using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// El catálogo de equipos respaldado por el CONTENIDO del CMS: sirve los <c>equipmentPage</c>
/// que el editor autoró, en vez del seed de demo.
/// </summary>
/// <remarks>
/// <para><b>Forma A del sub-spec S2</b> (doc 13 §5.bis): la colección es PROPIA —nadie más lee
/// equipos— y de sólo lectura —el producto no publica equipos desde la app—. La pregunta que lo
/// decide es <i>¿el almacén del que lee este vertical es suyo?</i>, y acá sí, así que la fuente
/// REEMPLAZA al seed y no lo siembra. Social necesitó la forma B justamente por lo contrario.</para>
///
/// <para>Es la ÚNICA clase de este eje que toca Umbraco. Las reglas viven en
/// <see cref="EquipmentContentRules"/>, que es pura; el proveedor y el controller siguen sin
/// saber que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Alquiler = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redespliegue.</para>
///
/// <para><b>Esta fuente NO siembra nada.</b> <c>ICatalogSource.GetAllAsync</c> se llama en CADA
/// búsqueda —el catálogo no cachea, a propósito, para que el read-your-writes salga gratis— así
/// que sembrar acá haría crecer un almacén ajeno una vez por búsqueda (#100).</para>
///
/// <para><b>Y no hay campo para el identificador del recurso de <c>Api.Booking</c></b>, por la
/// misma razón que en Salud (#118): ese identificador lo GENERA la capacidad y se resuelve
/// preguntando por el sujeto (<c>GET /v1/resources?subjectKind=&amp;subjectId=</c>, HU #25).</para>
/// </remarks>
public sealed class UmbracoEquipmentCatalogSource : ICatalogSource<RentalEquipment>
{
    internal const string Vertical = "Alquiler";
    private const string EquipmentPageAlias = "equipmentPage";
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _catalog;
    private readonly IOptionsMonitor<AlquilerSettings> _alquiler;
    private readonly ILogger<UmbracoEquipmentCatalogSource> _logger;

    /// <summary>Construye la fuente.</summary>
    /// <param name="umbracoContextAccessor">Para llegar al árbol publicado.</param>
    /// <param name="catalog">De dónde sale el scope del vertical.</param>
    /// <param name="alquiler">De dónde sale el tope de días del despliegue.</param>
    /// <param name="logger">Para decir qué se omitió y por qué.</param>
    public UmbracoEquipmentCatalogSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> catalog,
        IOptionsMonitor<AlquilerSettings> alquiler,
        ILogger<UmbracoEquipmentCatalogSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _catalog = catalog;
        _alquiler = alquiler;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las ocho fuentes anteriores: el scope de este catálogo no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Alquiler</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Los equipos servibles.</returns>
    public Task<IReadOnlyList<RentalEquipment>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        var equipos = new List<RentalEquipment>(nodes.Count);

        foreach (var node in nodes)
        {
            var proyectado = Project(node);
            if (proyectado is not null)
            {
                equipos.Add(proyectado);
            }
        }

        // Los slugs repetidos sólo se ven con el catálogo entero en la mano.
        Report(EquipmentContentRules.FindSlugCollisions(equipos.Select(e => e.Id)));

        var omitidos = nodes.Count - equipos.Count;
        if (omitidos > 0)
        {
            _logger.LogWarning(
                "UmbracoEquipmentCatalogSource: se omitieron {Omitidos} de {Total} equipmentPage "
                + "por datos incompletos.",
                omitidos, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<RentalEquipment>>(equipos);
    }

    /// <summary>Los <c>equipmentPage</c> publicados bajo el siteRoot configurado, o vacío.</summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            _logger.LogWarning("UmbracoEquipmentCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _catalog.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // Fallar CERRADO, como Salud: servir sin acotar mezclaría los equipos de todos los
            // siteRoots, y el de al lado puede alquilar otra cosa a otro precio.
            _logger.LogError(
                "UmbracoEquipmentCatalogSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError(
                "UmbracoEquipmentCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(EquipmentPageAlias).ToList();
    }

    /// <summary><c>equipmentPage</c> → <see cref="RentalEquipment"/>, o null si no es servible.</summary>
    private RentalEquipment? Project(IPublishedContent node)
    {
        var slug = node.Value<string>("equipmentSlug")?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            // Sin slug no hay identidad: es lo que viaja como subjectId hasta Api.Booking.
            _logger.LogWarning(
                "UmbracoEquipmentCatalogSource: equipmentPage id={Id} sin equipmentSlug; se omite.", node.Id);
            return null;
        }

        var name = node.Value<string>("equipmentName")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            // NO se cae al Name del nodo, por la razón de #118: el Name es del backoffice y sale
            // "Equipment Page (1)" en la tarjeta. Que falte la ficha se ve; un nombre de
            // andamiaje no.
            _logger.LogWarning(
                "UmbracoEquipmentCatalogSource: equipmentPage slug='{Slug}' sin equipmentName; se omite.", slug);
            return null;
        }

        var dias = EquipmentContentRules.ParseDayBounds(
            slug,
            node.Value<int>("equipmentMinDays"),
            node.Value<int>("equipmentMaxDays"),
            _alquiler.CurrentValue.MaxRentalDays);
        Report(dias.Issues);

        var tarifas = EquipmentContentRules.ParseRates(slug, ReadRates(node));
        Report(tarifas.Issues);

        return new RentalEquipment(
            Id: slug,
            Name: name,
            Category: node.Value<string>("equipmentCategory")?.Trim() ?? string.Empty,
            Summary: node.Value<string>("equipmentSummary")?.Trim() ?? string.Empty,
            Description: node.Value<string>("equipmentDescription")?.Trim() ?? string.Empty,
            CoverUrl: node.Value<IPublishedContent>("equipmentCover")?.Url(),
            GalleryUrls: node.Value<IEnumerable<IPublishedContent>>("equipmentGallery")
                ?.Select(m => m.Url()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Units: node.Value<int>("equipmentUnits"),
            DailyRate: node.Value<int>("equipmentDailyRate"),
            Deposit: node.Value<int>("equipmentDepositAmount"),
            MinDays: dias.Value.Min,
            MaxDays: dias.Value.Max,
            Includes: ReadLines(node, "equipmentIncludes"),
            Requirements: ReadLines(node, "equipmentRequirements"),
            Rates: tarifas.Value,
            Specs: ReadSpecs(node));
    }

    private static IReadOnlyList<string> ReadLines(IPublishedContent node, string alias)
        => node.Value<IEnumerable<string>>(alias)
            ?.Select(s => s?.Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>
    /// Los bloques de una propiedad BlockList, o vacío.
    /// </summary>
    /// <remarks>
    /// Un BlockList vacío llega como <c>null</c> y no como colección vacía; el caso está tratado
    /// por lo mismo que en <c>UmbracoCourseCatalogSource</c>.
    /// </remarks>
    private static IReadOnlyList<IPublishedElement> ReadBlocks(IPublishedContent node, string alias)
    {
        var blocks = node.Value<BlockListModel>(alias);
        return blocks is null || blocks.Count == 0
            ? Array.Empty<IPublishedElement>()
            : blocks.Select(b => b.Content).ToList();
    }

    private static IReadOnlyList<EquipmentRate> ReadRates(IPublishedContent node)
        => ReadBlocks(node, "equipmentRates")
            .Select(b => new EquipmentRate(
                Code: b.Value<string>("rateCode")?.Trim() ?? string.Empty,
                Label: b.Value<string>("rateLabel")?.Trim() ?? string.Empty,
                MinDays: b.Value<int>("rateMinDays"),
                PerDay: b.Value<int>("rateAmount"),
                Description: b.Value<string>("rateDescription")?.Trim() ?? string.Empty))
            .ToList();

    private static IReadOnlyList<EquipmentSpec> ReadSpecs(IPublishedContent node)
        => ReadBlocks(node, "equipmentSpecs")
            .Select(b => new EquipmentSpec(
                Label: b.Value<string>("specLabel")?.Trim() ?? string.Empty,
                Value: b.Value<string>("specValue")?.Trim() ?? string.Empty))
            .Where(s => s.Label.Length > 0 && s.Value.Length > 0)
            .ToList();

    private void Report(IReadOnlyList<EquipmentContentIssue> issues)
    {
        foreach (var i in issues)
        {
            if (i.Level == EquipmentContentIssueLevel.Error)
            {
                _logger.LogError("{Mensaje}", i.Message);
            }
            else
            {
                _logger.LogWarning("{Mensaje}", i.Message);
            }
        }
    }
}
