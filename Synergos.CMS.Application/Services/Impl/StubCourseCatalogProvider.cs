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

    // Mapping lessonId → contentItemId (id real asignado por el IContentStream al
    // sembrar la lección como Kind=lesson). Poblado una sola vez (idempotente).
    private Dictionary<string, string>? _lessonContentIds;

    public StubCourseCatalogProvider(IContentStream contentStream)
    {
        _contentStream = contentStream ?? throw new ArgumentNullException(nameof(contentStream));
    }

    public async Task<CourseSearchResult> SearchAsync(CourseQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureLessonsSeededAsync(cancellationToken);
        query ??= new CourseQuery();

        IEnumerable<AcademyDemoSeed.SeedCourse> filtered = AcademyDemoSeed.Courses;

        // 1) Filtro de texto (título / resumen / categoría / instructor).
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            filtered = filtered.Where(c =>
                c.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || c.Summary.Contains(text, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
                || AcademyDemoSeed.InstructorById(c.InstructorId).Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // 2) Filtro de categoría exacta (case-insensitive).
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            filtered = filtered.Where(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        // 3) Filtro de nivel exacto (case-insensitive).
        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            var level = query.Level.Trim();
            filtered = filtered.Where(c => string.Equals(c.Level, level, StringComparison.OrdinalIgnoreCase));
        }

        var matched = filtered
            .OrderByDescending(c => c.Rating)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();

        return new CourseSearchResult(matched, matched.Count);
    }

    public async Task<CourseDetail?> GetCourseAsync(string courseId, CancellationToken cancellationToken = default)
    {
        await EnsureLessonsSeededAsync(cancellationToken);

        var course = AcademyDemoSeed.Courses.FirstOrDefault(c =>
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
