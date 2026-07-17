using System.Collections.Concurrent;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICourseCatalogProvider"/> — catálogo del LMS (dominio
/// Educación) STUB para que el dominio corra end-to-end en demo sin un índice de
/// búsqueda real (mismo patrón stub-first que <c>StubProductCatalogProvider</c> /
/// <c>StubRoomAvailabilityProvider</c>). Sirve un catálogo sembrado en memoria
/// (varias categorías × cursos × módulos × lecciones), aplica el filtro de
/// texto/categoría/nivel y resuelve el detalle de un curso para la PDP-curso.
/// </summary>
/// <remarks>
/// <para>
/// <b>POLIMORFISMO Blogs (la pieza clave de la OLA).</b> El contenido editorial
/// de cada lección NO se duplica en el catálogo: al construirse, este stub
/// SIEMBRA cada lección en el <see cref="IContentStream"/> como item con
/// <c>Kind=lesson</c> (<see cref="IContentStream.CreateAsync"/>) y guarda el
/// mapping <c>lessonId → contentItemId</c>. La <see cref="CourseLesson.ContentItemId"/>
/// referencia ese item — el cuerpo/transcripción sale del MISMO motor de feed
/// que usa Blogs para sus posts, vía la abstracción (DIP, ADR 0002). Educación
/// depende de <see cref="IContentStream"/>, NO del módulo Blogs ni de su schema:
/// no instancia Blogs, reusa el seam. El siembre es idempotente (una sola vez por
/// instancia, con doble-checked lock).
/// </para>
/// <para>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). El adapter real (Examine sobre
/// <c>coursePage</c>/<c>lessonPage</c>, o un store LMS) implementa la misma seam
/// y se registra vía el composer sin tocar el motor ni el módulo Angular. ADR 0075.
/// </para>
/// </remarks>
public sealed class StubCourseCatalogProvider : ICourseCatalogProvider
{
    private readonly IContentStream _contentStream;
    private readonly object _seedLock = new();

    // Cara de LECTURA del motor de matrícula (alumnos + ingresos por curso) para
    // el panel del instructor. Opcional (null = métricas en cero): se enchufa por
    // property injection en el composer DESPUÉS de construir ambos singletons, para
    // no crear un ciclo catálogo↔matrícula en el ctor (DIP). No lo toca el flujo
    // del catálogo; solo GetForInstructorAsync lo consulta.
    public IEnrollmentMetrics? EnrollmentMetrics { get; set; }

    // Mapping lessonId → contentItemId (id real asignado por el IContentStream al
    // sembrar la lección como Kind=lesson). Poblado una sola vez (idempotente).
    private Dictionary<string, string>? _lessonContentIds;

    // Cursos publicados por instructores en runtime (PublishCourseAsync), keyed por
    // id. Aditivo al catálogo sembrado (AcademyDemoSeed.Courses): el search y el
    // detalle recorren ambos. El estado vive en el proceso, igual que el resto de
    // stubs del motor. Sus lecciones se siembran al feed al publicar.
    private readonly ConcurrentDictionary<string, AcademyDemoSeed.SeedCourse> _published = new(StringComparer.OrdinalIgnoreCase);

    // Contador monotónico para el id de cursos publicados por instructores.
    private static int _publishedCounter;

    private readonly ICatalogIndex<AcademyDemoSeed.SeedCourse> _index;

    /// <summary>
    /// Ctor de 1 argumento. <b>Se conserva tal cual a propósito:</b> lo usa la factory
    /// EXPLÍCITA del composer, que a su vez es lo que permite la property injection de
    /// <see cref="EnrollmentMetrics"/> sobre ESA MISMA instancia. Añadir aquí un ctor más
    /// goloso sin conservar éste haría que el DI eligiera otro y la inyección aterrizara en
    /// una instancia distinta → el panel del instructor mostraría 0 alumnos y $0, en
    /// silencio y con los tests en verde (los tests cablean la inyección ellos mismos).
    /// </summary>
    public StubCourseCatalogProvider(IContentStream contentStream)
        : this(contentStream, new InMemoryCatalogIndex<AcademyDemoSeed.SeedCourse>(Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal StubCourseCatalogProvider(IContentStream contentStream, ICatalogIndex<AcademyDemoSeed.SeedCourse> index)
    {
        _contentStream = contentStream ?? throw new ArgumentNullException(nameof(contentStream));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    // Vista unificada del catálogo: cursos sembrados + publicados en runtime.
    private IEnumerable<AcademyDemoSeed.SeedCourse> AllCourses()
        => AcademyDemoSeed.Courses.Concat(_published.Values);

    /// <summary>
    /// Lo que Educación DECLARA sobre su catálogo. Cero lógica.
    /// </summary>
    /// <remarks>
    /// <b>El instructor entra como un campo buscable más</b>, resuelto con un lambda: el
    /// motor no sabe que existen instructores, solo que hay un campo con peso 3. Buscar
    /// "Elena" y encontrar sus cursos es intención real y hay que preservarla — el nombre
    /// del instructor no aparece en ningún título ni resumen.
    ///
    /// <para>El resumen pesa lo mínimo: es prosa larga que casa por accidente y no debe
    /// desplazar nunca a un título.</para>
    /// </remarks>
    internal static CatalogDescriptor<AcademyDemoSeed.SeedCourse> Descriptor { get; } = new(
        idOf: c => c.Id,
        searchFields: new[]
        {
            new CatalogSearchField<AcademyDemoSeed.SeedCourse>(5, c => c.Title),
            new CatalogSearchField<AcademyDemoSeed.SeedCourse>(3, c => AcademyDemoSeed.InstructorById(c.InstructorId).Name),
            new CatalogSearchField<AcademyDemoSeed.SeedCourse>(2, c => c.Category),
            new CatalogSearchField<AcademyDemoSeed.SeedCourse>(1, c => c.Summary),
        },
        // El orden histórico: mejor calificados primero.
        defaultOrder: courses => courses.OrderByDescending(c => c.Rating).ThenBy(c => c.Title, StringComparer.Ordinal),
        filters: new CatalogFilter<AcademyDemoSeed.SeedCourse>[]
        {
            new CatalogTermFilter<AcademyDemoSeed.SeedCourse>("category", "Escuela", c => new[] { c.Category }),
            new CatalogTermFilter<AcademyDemoSeed.SeedCourse>("level", "Nivel", c => new[] { c.Level }),
        });

    public async Task<CourseSearchResult> SearchAsync(CourseQuery query, CancellationToken cancellationToken = default)
    {
        // La siembra se queda AQUÍ, como primer await y fuera del motor: es un efecto
        // lateral async (crea el item del feed de Blogs por cada lección) y el motor es
        // síncrono y declarado función PURA. Meterla dentro sembraría en cada búsqueda o
        // —peor— dejaría de sembrarse en los caminos que no pasan por el catálogo, y el
        // course-player mostraría lecciones sin cuerpo.
        await EnsureLessonsSeededAsync(cancellationToken);
        query ??= new CourseQuery();

        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            filters["category"] = new[] { query.Category.Trim() };
        }
        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            filters["level"] = new[] { query.Level.Trim() };
        }

        // AllCourses() = seed + publicados en runtime, leído fresco: es el read-your-writes
        // del instructor que acaba de publicar. Take explícito + Unpaged en el ctor porque
        // esta seam promete TODOS los cursos que casan, no una página.
        var result = _index.Search(
            AllCourses().ToList(),
            new CatalogQuery(Text: query.Text, Filters: filters.Count > 0 ? filters : null, Take: int.MaxValue));

        var matched = result.Items.Select(ToSummary).ToList();
        return new CourseSearchResult(matched, result.Total);
    }

    public async Task<CourseDetail?> GetCourseAsync(string courseId, CancellationToken cancellationToken = default)
    {
        await EnsureLessonsSeededAsync(cancellationToken);

        var course = AllCourses().FirstOrDefault(c =>
            string.Equals(c.Id, courseId, StringComparison.OrdinalIgnoreCase));
        if (course is null)
        {
            return null;
        }

        var modules = course.Modules
            .OrderBy(m => m.Order)
            .Select(m => new CourseModule(
                Id: m.Id,
                Title: m.Title,
                Order: m.Order,
                Lessons: m.Lessons
                    .OrderBy(l => l.Order)
                    .Select(ToLesson)
                    .ToList()))
            .ToList();

        // Planes de precio: contado + (para los de pago) un plan de 3 cuotas con
        // recargo del 8% (regla de negocio aislada — análoga a la política de
        // cancelación de Hoteles). Los gratuitos solo tienen "inscripción gratis".
        var plans = BuildPlans(course);

        return new CourseDetail(
            Course: ToSummary(course),
            Description: course.Description,
            Outcomes: course.Outcomes,
            Instructor: AcademyDemoSeed.InstructorById(course.InstructorId),
            Modules: modules,
            Plans: plans);
    }

    // ── Cara de AUTOR: panel del instructor + publicar curso ────────────

    public async Task<InstructorCoursesResult> GetForInstructorAsync(string instructorId, CancellationToken cancellationToken = default)
    {
        await EnsureLessonsSeededAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(instructorId))
        {
            return new InstructorCoursesResult(instructorId ?? string.Empty, Array.Empty<InstructorCourse>(), 0, 0m, AcademyDemoSeed.Currency);
        }

        var id = instructorId.Trim();
        var owned = AllCourses()
            .Where(c => string.Equals(c.InstructorId, id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Rating)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .ToList();

        var rows = new List<InstructorCourse>(owned.Count);
        var totalStudents = 0;
        var totalRevenue = 0m;
        foreach (var course in owned)
        {
            // Las métricas de matrícula (alumnos + ingresos) las provee el motor de
            // matrícula por composición (IEnrollmentMetrics) — el catálogo no las
            // duplica. Sin el seam enchufado, ceros (los cursos aún se listan).
            var stats = EnrollmentMetrics is not null
                ? await EnrollmentMetrics.GetCourseStatsAsync(course.Id, cancellationToken)
                : new CourseEnrollmentStats(0, 0m);

            rows.Add(new InstructorCourse(
                Course: ToSummary(course),
                Metrics: new CourseMetrics(stats.Students, stats.Revenue, AcademyDemoSeed.Currency, course.Rating)));
            totalStudents += stats.Students;
            totalRevenue += stats.Revenue;
        }

        return new InstructorCoursesResult(id, rows, totalStudents, totalRevenue, AcademyDemoSeed.Currency);
    }

    public async Task<CourseDetail> PublishCourseAsync(CourseDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ArgumentException("El título del curso es obligatorio.", nameof(draft));
        }
        if (draft.Modules is null || draft.Modules.Count == 0)
        {
            throw new ArgumentException("El curso requiere al menos un módulo.", nameof(draft));
        }
        if (draft.Modules.Any(m => m is null || m.Lessons is null || m.Lessons.Count == 0))
        {
            throw new ArgumentException("Cada módulo requiere al menos una lección.", nameof(draft));
        }

        await EnsureLessonsSeededAsync(cancellationToken);

        // Id/slug estables asignados por el catálogo (el adapter real usa el del CMS).
        var courseId = $"course-org-{Interlocked.Increment(ref _publishedCounter)}";
        var instructorId = string.IsNullOrWhiteSpace(draft.InstructorId) ? "ins-desconocido" : draft.InstructorId.Trim();
        var title = draft.Title.Trim();
        var category = string.IsNullOrWhiteSpace(draft.Category) ? "General" : draft.Category.Trim();

        // Construye módulos → lecciones con ids estables + siembra cada lección al
        // MISMO feed que usa Blogs (Kind=lesson) — polimorfismo, no instanciación.
        var seedModules = new List<AcademyDemoSeed.SeedModule>(draft.Modules.Count);
        var newLessonIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var moduleOrder = 1;
        foreach (var m in draft.Modules)
        {
            var lessonOrder = 1;
            var seedLessons = new List<AcademyDemoSeed.SeedLesson>(m.Lessons.Count);
            foreach (var l in m.Lessons)
            {
                if (l is null || string.IsNullOrWhiteSpace(l.Title))
                {
                    throw new ArgumentException("Cada lección requiere un título.", nameof(draft));
                }

                var lessonId = $"{courseId}-l{moduleOrder}-{lessonOrder}";
                var contentBody = string.IsNullOrWhiteSpace(l.ContentBody)
                    ? $"Lección: {l.Title.Trim()}"
                    : l.ContentBody.Trim();

                var item = await _contentStream.CreateAsync(
                    new NewContentItem(
                        AuthorId: instructorId,
                        Body: contentBody,
                        MediaUrl: l.VideoUrl,
                        Kind: "lesson"),
                    cancellationToken);
                newLessonIds[lessonId] = item.Id;

                seedLessons.Add(new AcademyDemoSeed.SeedLesson(
                    Id: lessonId,
                    Title: l.Title.Trim(),
                    Order: lessonOrder,
                    DurationMinutes: Math.Max(0, l.DurationMinutes),
                    VideoRef: l.VideoUrl,
                    IsPreview: lessonOrder == 1 && moduleOrder == 1, // 1ª lección = preview
                    ContentBody: contentBody,
                    Resources: Array.Empty<CourseResource>()));
                lessonOrder++;
            }

            seedModules.Add(new AcademyDemoSeed.SeedModule(
                Id: $"{courseId}-m{moduleOrder}",
                Title: string.IsNullOrWhiteSpace(m.Title) ? $"Módulo {moduleOrder}" : m.Title.Trim(),
                Order: moduleOrder,
                Lessons: seedLessons));
            moduleOrder++;
        }

        var seedCourse = new AcademyDemoSeed.SeedCourse(
            Id: courseId,
            Title: title,
            Summary: string.IsNullOrWhiteSpace(draft.Summary) ? title : draft.Summary.Trim(),
            Description: string.IsNullOrWhiteSpace(draft.Description) ? string.Empty : draft.Description.Trim(),
            Category: category,
            Level: string.IsNullOrWhiteSpace(draft.Level) ? "Principiante" : draft.Level.Trim(),
            InstructorId: instructorId,
            CoverImageUrl: draft.CoverImageUrl,
            Price: Math.Max(0m, draft.Price),
            Rating: 0.0, // sin reseñas todavía
            Outcomes: draft.Outcomes ?? Array.Empty<string>(),
            Modules: seedModules);

        _published[courseId] = seedCourse;

        // Publica el mapping de las lecciones nuevas para que ToLesson resuelva su
        // ContentItemId (sin re-sembrar el catálogo entero).
        lock (_seedLock)
        {
            var map = _lessonContentIds is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(_lessonContentIds, StringComparer.Ordinal);
            foreach (var kv in newLessonIds)
            {
                map[kv.Key] = kv.Value;
            }
            _lessonContentIds = map;
        }

        return (await GetCourseAsync(courseId, cancellationToken))!;
    }

    // ── Siembra POLIMÓRFICA de lecciones en el IContentStream (Kind=lesson) ──

    private async Task EnsureLessonsSeededAsync(CancellationToken cancellationToken)
    {
        if (_lessonContentIds is not null)
        {
            return;
        }

        // Crea los items fuera del lock (CreateAsync es async), luego publica el
        // mapping bajo lock con doble check para no sembrar dos veces.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var course in AcademyDemoSeed.Courses)
        {
            foreach (var module in course.Modules)
            {
                foreach (var lesson in module.Lessons)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // El cuerpo editorial de la lección entra al MISMO motor de feed
                    // que usa Blogs, con Kind=lesson — polimorfismo, no instanciación.
                    var item = await _contentStream.CreateAsync(
                        new NewContentItem(
                            AuthorId: course.InstructorId,
                            Body: lesson.ContentBody,
                            MediaUrl: lesson.VideoRef,
                            Kind: "lesson"),
                        cancellationToken);
                    map[lesson.Id] = item.Id;
                }
            }
        }

        lock (_seedLock)
        {
            _lessonContentIds ??= map;
        }
    }

    private CourseLesson ToLesson(AcademyDemoSeed.SeedLesson l)
    {
        // El ContentItemId es el id real del item Kind=lesson del IContentStream
        // (poblado por la siembra). Fallback al lessonId si el mapping no resolvió
        // (defensivo — nunca debería pasar tras EnsureLessonsSeededAsync).
        var contentItemId = _lessonContentIds is not null && _lessonContentIds.TryGetValue(l.Id, out var id)
            ? id
            : l.Id;

        return new CourseLesson(
            Id: l.Id,
            Title: l.Title,
            Order: l.Order,
            DurationMinutes: l.DurationMinutes,
            VideoRef: l.VideoRef,
            ContentItemId: contentItemId,
            Resources: l.Resources,
            IsPreview: l.IsPreview);
    }

    private static CourseSummary ToSummary(AcademyDemoSeed.SeedCourse c) => new(
        Id: c.Id,
        Title: c.Title,
        Summary: c.Summary,
        Category: c.Category,
        Level: c.Level,
        InstructorName: AcademyDemoSeed.InstructorById(c.InstructorId).Name,
        CoverImageUrl: c.CoverImageUrl,
        Price: c.Price,
        Currency: AcademyDemoSeed.Currency,
        IsFree: c.IsFree,
        Rating: c.Rating,
        LessonCount: c.LessonCount,
        DurationMinutes: c.DurationMinutes);

    private static IReadOnlyList<CoursePricingPlan> BuildPlans(AcademyDemoSeed.SeedCourse c)
    {
        if (c.IsFree)
        {
            return new[]
            {
                new CoursePricingPlan("free", "Inscripción gratuita", 0m, AcademyDemoSeed.Currency, 1),
            };
        }

        // Contado (1 cuota) + 3 cuotas con recargo del 8% (EMI). El recargo se
        // redondea a entero (patrón visual COP, sin decimales).
        var installmentTotal = decimal.Round(c.Price * 1.08m, 0, MidpointRounding.AwayFromZero);
        return new[]
        {
            new CoursePricingPlan("full", "Pago de contado", c.Price, AcademyDemoSeed.Currency, 1),
            new CoursePricingPlan("emi-3", "3 cuotas", installmentTotal, AcademyDemoSeed.Currency, 3),
        };
    }
}
