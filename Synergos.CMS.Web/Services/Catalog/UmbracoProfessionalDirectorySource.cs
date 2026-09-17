using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// El directorio de profesionales de Salud respaldado por el CONTENIDO del CMS: sirve los
/// <c>professionalPage</c> que el editor autoró, en vez del staff sembrado.
/// </summary>
/// <remarks>
/// <b>Salud era el ÚNICO vertical sin el eje 1 del molde</b> (doc 12 §7.2, #118): el profesional
/// salía de <c>StubDoctorDirectory</c>, sembrado en C#, así que dar de alta un médico era un
/// cambio de código y un despliegue. Es lo mismo que le pasaba a Educación antes de la HU #100.
///
/// <para>Es la ÚNICA clase de este eje que toca Umbraco. Las reglas viven en
/// <see cref="ProfessionalContentRules"/>, que es pura; el directorio y el controller siguen sin
/// saber que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Salud = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redespliegue.</para>
///
/// <para><b>Y NO hay campo para el identificador del recurso, a propósito.</b> Lo que viaja al
/// orquestador es el <see cref="MedicalDoctor.Id"/> —el slug que escribió el editor— como
/// <c>professionalId</c>; el identificador interno del recurso lo GENERA <c>Api.Booking</c> y se
/// resuelve preguntando por el sujeto (<c>GET /v1/resources?subjectKind=&amp;subjectId=</c>,
/// HU #25). Un campo aquí para escribirlo a mano sería <c>SaludSettings.ResourceIdPrefix</c> otra
/// vez: la convención que ninguna convención puede acertar (doc 12 §6.5).</para>
///
/// <para><b>Esta fuente NO siembra nada</b>, y es la diferencia con Educación: un profesional no
/// tiene cuerpos que referenciar desde otro almacén, así que no hay <c>ContentItemId</c> que
/// resolver. Aunque lo hubiera, sembrar aquí sería el defecto caro —
/// <c>ICatalogSource.GetAllAsync</c> se llama en CADA búsqueda, así que el feed crecería un ítem
/// por lección y por búsqueda (#100).</para>
/// </remarks>
public sealed class UmbracoProfessionalDirectorySource : ICatalogSource<MedicalDoctor>
{
    internal const string Vertical = "Salud";
    private const string ProfessionalPageAlias = "professionalPage";
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ILogger<UmbracoProfessionalDirectorySource> _logger;

    public UmbracoProfessionalDirectorySource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ILogger<UmbracoProfessionalDirectorySource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las otras cinco fuentes: el scope de este directorio no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Salud</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    public Task<IReadOnlyList<MedicalDoctor>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        var profesionales = new List<MedicalDoctor>(nodes.Count);

        foreach (var node in nodes)
        {
            var proyectado = Project(node);
            if (proyectado is not null)
            {
                profesionales.Add(proyectado);
            }
        }

        // Los slugs repetidos sólo se ven con el directorio entero en la mano, así que la
        // comprobación va acá y no dentro de la proyección de cada nodo.
        Report(ProfessionalContentRules.FindSlugCollisions(profesionales.Select(p => p.Id)));

        var skipped = nodes.Count - profesionales.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "UmbracoProfessionalDirectorySource: se omitieron {Skipped} de {Total} "
                + "professionalPage por datos incompletos.",
                skipped, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<MedicalDoctor>>(profesionales);
    }

    /// <summary>
    /// Los <c>professionalPage</c> publicados bajo el siteRoot configurado, o vacío.
    /// </summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            _logger.LogWarning(
                "UmbracoProfessionalDirectorySource: sin UmbracoContext; se sirve directorio vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // Fallar CERRADO: servir sin acotar mezclaría los profesionales de todos los
            // siteRoots, y el de al lado puede ser un abogado.
            _logger.LogError(
                "UmbracoProfessionalDirectorySource: falta Synergos:Catalog:Scopes:{Vertical}. "
                + "NO se sirve directorio.",
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
                "UmbracoProfessionalDirectorySource: no existe un siteRoot con brandKey '{BrandKey}'.",
                brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(ProfessionalPageAlias).ToList();
    }

    /// <summary>
    /// <c>professionalPage</c> → <see cref="MedicalDoctor"/>, o null si no es servible.
    /// </summary>
    private MedicalDoctor? Project(IPublishedContent node)
    {
        var slug = node.Value<string>("professionalSlug")?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            // Sin slug no hay identidad: es lo que el portal resuelve y lo que viaja como
            // subjectId hasta Api.Booking. Un profesional sin él no se puede agendar.
            _logger.LogWarning(
                "UmbracoProfessionalDirectorySource: professionalPage id={Id} sin professionalSlug; se omite.",
                node.Id);
            return null;
        }

        var name = node.Value<string>("professionalName")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            // NO se cae al Name del nodo: el Name es del árbol del backoffice y sale
            // "Professional Page (1)" en la tarjeta del directorio. Que falte la ficha se ve;
            // un nombre de andamiaje no — y quien elige médico lo elige por el nombre.
            _logger.LogWarning(
                "UmbracoProfessionalDirectorySource: professionalPage slug='{Slug}' sin professionalName; se omite.",
                slug);
            return null;
        }

        var dias = ProfessionalContentRules.ParseWorkingDays(
            slug, node.Value<IEnumerable<string>>("professionalWorkingDays"));
        Report(dias.Issues);

        var franja = ProfessionalContentRules.ParseSlotWindow(
            slug,
            node.Value<int>("professionalSlotStartHour"),
            node.Value<int>("professionalSlotEndHour"));
        Report(franja.Issues);

        var minutos = ProfessionalContentRules.ParseSlotMinutes(
            slug, node.Value<int>("professionalSlotMinutes"));
        Report(minutos.Issues);

        var admite = ProfessionalContentRules.ParseAcceptingPatients(
            slug, node.Value<string>("professionalAcceptingPatients"));
        Report(admite.Issues);

        return new MedicalDoctor(
            Id: slug,
            FullName: name,
            Specialty: node.Value<string>("professionalSpecialty")?.Trim() ?? string.Empty,
            LicenseNumber: node.Value<string>("professionalLicense")?.Trim() ?? string.Empty,
            // El rating es del motor de reseñas, no del editor — lo mismo que en coursePage
            // (#100): dejar que se lo ponga a mano es dejar que se ponga 5. Sale 0 hasta que
            // haya quién lo calcule, y 0 es «no hay reseñas», no «le pusieron cero».
            Rating: 0d,
            YearsExperience: node.Value<int>("professionalYearsExperience"),
            AvatarUrl: node.Value<IPublishedContent>("professionalPhoto")?.Url(),
            WorkingDays: dias.Value,
            SlotStartHour: franja.Value.Start,
            SlotEndHour: franja.Value.End,
            SlotMinutes: minutos.Value,
            Phone: node.Value<string>("professionalPhone")?.Trim() ?? string.Empty,
            Email: node.Value<string>("professionalEmail")?.Trim() ?? string.Empty,
            AcceptingPatients: admite.Value);
    }

    private void Report(IReadOnlyList<ProfessionalContentIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Level == ProfessionalContentIssueLevel.Error)
            {
                _logger.LogError("UmbracoProfessionalDirectorySource: {Issue}", issue.Message);
            }
            else
            {
                _logger.LogWarning("UmbracoProfessionalDirectorySource: {Issue}", issue.Message);
            }
        }
    }
}
