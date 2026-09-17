using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// La fuente del catálogo de Educación respaldada por el CONTENIDO del CMS: sirve los
/// <c>coursePage</c> que el editor autoró, en vez del seed hardcodeado.
/// </summary>
/// <remarks>
/// <b>Calco de <see cref="UmbracoEventCatalogSource"/></b>, aplicado al vertical que peor
/// estaba: Educación era el único cuyo objeto central —un curso— no se podía autorar (#100).
/// Los cursos salían de <c>StubCourseCatalogProvider</c>, sembrados en C#, así que publicar un
/// curso exigía un despliegue.
///
/// <para>Es la ÚNICA clase de Educación que toca Umbraco. El motor, el descriptor y las reglas
/// de precio siguen en Application sin saber que esto existe (ADR 0002); las reglas de leer el
/// contenido están en <see cref="CourseContentRules"/>, que es lógica pura y se prueba sin un
/// contexto de Umbraco.</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Academy = cms</c></b> y el rollback es
/// esa misma línea a <c>demo</c>, sin redeploy.</para>
///
/// <para><b>Emite <see cref="AuthoredCourse"/> y no <see cref="CourseDetail"/> a secas</b>, y
/// ésa es la diferencia con las otras cuatro fuentes. Una lección del contrato referencia su
/// cuerpo por <see cref="CourseLesson.ContentItemId"/> —un id que asigna el feed al sembrar—,
/// así que la fuente no puede rellenarlo: entrega el cuerpo y quien siembra resuelve el id.
/// Sembrar aquí sería peor que incompleto: <c>GetAllAsync</c> se llama en CADA búsqueda, así
/// que el feed crecería un item por lección y por búsqueda.</para>
/// </remarks>
public sealed class UmbracoCourseCatalogSource : ICatalogSource<AuthoredCourse>
{
    internal const string Vertical = "Academy";
    private const string CoursePageAlias = "coursePage";
    private const string SiteRootAlias = "siteRoot";

    /// <summary>
    /// Moneda del catálogo. Constante y no schema, por lo mismo que en Eventos: un deploy es un
    /// origen y todo el motor de Educación ya emite COP. Dejárselo escribir al editor garantiza
    /// "cop"/"COP "/"pesos" en el mismo catálogo.
    /// </summary>
    private const string Currency = "COP";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ILogger<UmbracoCourseCatalogSource> _logger;

    public UmbracoCourseCatalogSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ILogger<UmbracoCourseCatalogSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las otras cuatro fuentes: el scope de este catálogo no es del
    /// request sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Academy</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    public Task<IReadOnlyList<AuthoredCourse>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        var courses = nodes
            .Select(Project)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        var skipped = nodes.Count - courses.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "UmbracoCourseCatalogSource: se omitieron {Skipped} de {Total} coursePage por datos incompletos.",
                skipped, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<AuthoredCourse>>(courses);
    }

    /// <summary>
    /// Los <c>coursePage</c> publicados bajo el siteRoot configurado, o vacío.
    /// </summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            _logger.LogWarning("UmbracoCourseCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // Fallar CERRADO: servir sin acotar mezclaría los cursos de todos los siteRoots.
            _logger.LogError(
                "UmbracoCourseCatalogSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoCourseCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(CoursePageAlias).ToList();
    }

    /// <summary>
    /// <c>coursePage</c> → <see cref="AuthoredCourse"/>, o null si no es servible.
    /// </summary>
    private AuthoredCourse? Project(IPublishedContent node)
    {
        var slug = node.Value<string>("courseSlug")?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            // Sin slug no hay identidad: es lo que GetCourseAsync resuelve y lo que lleva la URL
            // de la ficha. Un curso sin él es un enlace roto.
            _logger.LogWarning("UmbracoCourseCatalogSource: coursePage id={Id} sin courseSlug; se omite.", node.Id);
            return null;
        }

        var title = node.Value<string>("courseTitle")?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            // NO se cae al Name del nodo: el Name es del árbol del backoffice y sale "Curso (1)"
            // en la tarjeta. Que falte la ficha se ve; un título de andamiaje no.
            _logger.LogWarning("UmbracoCourseCatalogSource: coursePage slug='{Slug}' sin courseTitle; se omite.", slug);
            return null;
        }

        if (!CourseContentRules.TryParsePrice(node.Value<string>("coursePriceFrom"), out var price))
        {
            _logger.LogError(
                "UmbracoCourseCatalogSource: curso '{Slug}' tiene coursePriceFrom='{Raw}', que no es un precio "
                + "inequívoco. Se OMITE del catálogo. Formato: SOLO DÍGITOS, sin puntos ni símbolos "
                + "(ej. 180000, no \"180.000\" — eso se leería como 180 pesos, y el motor de matrícula "
                + "cobra lo que dice el catálogo).",
                slug, node.Value<string>("coursePriceFrom"));
            return null;
        }

        var level = CourseContentRules.NormalizeLevel(slug, node.Value<string>("courseLevel"));
        Report(level.Issues);

        var curriculum = CourseContentRules.BuildCurriculum(slug, ReadModuleDrafts(node));
        Report(curriculum.Issues);

        var (lessonCount, durationMinutes) = CourseContentRules.Aggregate(
            curriculum.Value,
            node.Value<int>("courseLessonCount"),
            node.Value<int>("courseDurationMinutes"));

        var instructorName = node.Value<string>("courseInstructorName")?.Trim() ?? string.Empty;
        var summary = new CourseSummary(
            Id: slug,
            Title: title,
            Summary: node.Value<string>("courseSubtitle")?.Trim() ?? string.Empty,
            Category: string.IsNullOrWhiteSpace(node.Value<string>("courseCategory"))
                ? "General"
                : node.Value<string>("courseCategory")!.Trim(),
            Level: level.Value,
            InstructorName: instructorName,
            CoverImageUrl: node.Value<IPublishedContent>("courseCover")?.Url(),
            Price: price,
            Currency: Currency,
            IsFree: price <= 0m,
            // El rating es del motor de reseñas, no del editor: un curso recién autorado no
            // tiene ninguna, y dejar que se lo ponga a mano es dejar que se ponga 5.
            Rating: 0d,
            LessonCount: lessonCount,
            DurationMinutes: durationMinutes,
            // Lo que llega aquí está PUBLICADO, y no por convención: esta fuente recorre el
            // caché de contenido publicado, así que un coursePage guardado sin publicar no
            // aparece en `ResolveNodes`. Decirlo explícitamente es lo que evita que el otro
            // lado lo suponga — su normalizador degrada a «publicado» cuando la clave falta,
            // que es lo mismo pero sin que nadie lo haya decidido (#102).
            Status: CourseStatuses.Published,
            // `UpdateDate` en el caché publicado es la fecha de la ÚLTIMA publicación del
            // nodo, no la de la primera: Umbraco no expone la primera por aquí. Para ordenar
            // «Más recientes» es dato de verdad —y es lo que la Tienda ya hace en
            // `DefaultShopQuery`—, y para etiquetar «publicado el» dice la última vez que se
            // publicó, que también es cierto. **Disparador** para dejar de usarla: que
            // alguien necesite la fecha de la PRIMERA publicación, que hay que guardar
            // (campo del schema o histórico) porque no se deduce de aquí.
            PublishedAt: DateOnly.FromDateTime(node.UpdateDate));

        var detail = new CourseDetail(
            Course: summary,
            Description: node.Value<string>("courseDescription")?.Trim() ?? string.Empty,
            Outcomes: ReadTextList(node, "courseOutcomes"),
            Instructor: new CourseInstructor(
                // El instructor todavía no es una entidad con ficha propia: el schema lo modela
                // como tres campos del curso. El id se deriva del nombre para que el panel de
                // instructor agrupe sus cursos, y se vuelve una referencia de verdad el día que
                // haya un instructorPage.
                Id: string.IsNullOrWhiteSpace(instructorName)
                    ? $"ins-{slug}"
                    : $"ins-{CourseContentRules.Slugify(instructorName)}",
                Name: instructorName,
                Headline: node.Value<string>("courseInstructorHeadline")?.Trim() ?? string.Empty,
                Bio: node.Value<string>("courseInstructorBio")?.Trim() ?? string.Empty,
                AvatarUrl: null),
            Modules: Array.Empty<CourseModule>(),
            Plans: Synergos.CMS.Application.Services.Impl.CoursePricingRules.Build(price, Currency));

        return new AuthoredCourse(detail, curriculum.Value);
    }

    private void Report(IReadOnlyList<CourseContentIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Level == CourseContentIssueLevel.Error)
            {
                _logger.LogError("UmbracoCourseCatalogSource: {Issue}", issue.Message);
            }
            else
            {
                _logger.LogWarning("UmbracoCourseCatalogSource: {Issue}", issue.Message);
            }
        }
    }

    private static IReadOnlyList<AuthoredModuleDraft> ReadModuleDrafts(IPublishedContent node)
        => ReadBlocks(node, "courseModules")
            .Select(m => new AuthoredModuleDraft(
                Id: m.Value<string>("moduleId"),
                Title: m.Value<string>("moduleTitle"),
                Lessons: ReadBlocks(m, "moduleLessons")
                    .Select(l => new AuthoredLessonDraft(
                        Id: l.Value<string>("lessonId"),
                        Title: l.Value<string>("lessonTitle"),
                        DurationMinutes: l.Value<int>("lessonDurationMinutes"),
                        VideoUrl: l.Value<Link>("lessonVideo")?.Url,
                        Body: l.Value<string>("lessonBody"),
                        Resources: ReadLinks(l, "lessonResources"),
                        IsPreview: l.Value<bool>("lessonIsPreview")))
                    .ToList()))
            .ToList();

    /// <summary>
    /// Los bloques de una propiedad BlockList, o vacío si la propiedad no está poblada.
    /// </summary>
    /// <remarks>
    /// Un BlockList vacío llega como null, no como colección vacía, y ese null es la diferencia
    /// entre «el editor no cargó el temario» y una <c>NullReferenceException</c> por request.
    /// Toma <see cref="IPublishedElement"/> y no <see cref="IPublishedContent"/> para poder
    /// bajar al segundo nivel: las lecciones son un BlockList DENTRO de un bloque de módulo.
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

    private static IReadOnlyList<AuthoredResourceDraft> ReadLinks(IPublishedElement node, string alias)
    {
        var links = node.Value<IEnumerable<Link>>(alias);
        if (links is null)
        {
            return Array.Empty<AuthoredResourceDraft>();
        }

        return links
            .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Url))
            .Select(l => new AuthoredResourceDraft(l.Name, l.Url))
            .ToList();
    }

    private static IReadOnlyList<string> ReadTextList(IPublishedElement node, string alias)
    {
        var raw = node.Value<IEnumerable<string>>(alias);
        if (raw is null)
        {
            return Array.Empty<string>();
        }

        return raw.Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).ToList();
    }
}
