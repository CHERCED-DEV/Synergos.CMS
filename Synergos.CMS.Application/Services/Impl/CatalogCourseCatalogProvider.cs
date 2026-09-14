using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El catálogo de Educación servido desde el CONTENIDO que el editor autoró, en vez del seed
/// hardcodeado. Es el <see cref="ICourseCatalogProvider"/> que se registra cuando
/// <c>Synergos:Catalog:Sources:Academy = cms</c>.
/// </summary>
/// <remarks>
/// <b>Dos capas, y el orden entre ellas es la decisión</b> — calco de
/// <see cref="CatalogEventCatalogProvider"/>. Debajo está el contenido
/// (<see cref="ICatalogSource{T}"/> de <see cref="AuthoredCourse"/>, que en producción es
/// <c>UmbracoCourseCatalogSource</c>); encima, los cursos que publican INSTRUCTORES desde el
/// panel, que no pasan por el backoffice. La capa de arriba gana: publicar y no ver el cambio
/// es peor que la ambigüedad.
///
/// <para><b>Y una tercera cosa que Eventos no necesita: SEMBRAR.</b> Una lección referencia su
/// cuerpo por <see cref="CourseLesson.ContentItemId"/> —un item del
/// <see cref="IContentStream"/> con <c>Kind=lesson</c>— y ese id lo asigna el feed. La fuente
/// de contenido no lo puede saber, así que entrega el cuerpo y esta clase lo siembra.</para>
///
/// <para><b>La siembra es durable, y tiene que serlo.</b> El stub la resuelve con un
/// diccionario en memoria porque su catálogo también vive ahí; aquí el catálogo lo edita una
/// persona y el proceso se reinicia. Con un mapping en memoria, cada arranque re-sembraría el
/// feed entero: el mismo curso con sus lecciones duplicadas una vez por despliegue, creciendo
/// para siempre y sin que nada fallara.</para>
///
/// <para>Lógica pura: cero <c>Umbraco.Cms.*</c> y cero <c>Microsoft.AspNetCore.*</c>
/// (ADR 0002). La única clase que toca Umbraco es la fuente que se le inyecta.</para>
/// </remarks>
public sealed class CatalogCourseCatalogProvider : ICourseCatalogProvider
{
    /// <summary>Familia del store para los cursos publicados por instructores.</summary>
    public const string ResourceType = "course-catalog";

    /// <summary>
    /// Familia del store para el mapping <c>lessonId → item del feed</c>.
    /// </summary>
    public const string LessonContentResourceType = "course-lesson-content";

    private const string GeneratedIdPrefix = "course-org-";
    private const int MaxKeyLength = 96;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICatalogSource<AuthoredCourse> _source;
    private readonly IJsonEntityStore _store;
    private readonly IContentStream _contentStream;
    private readonly ICatalogIndex<CourseSummary> _index;
    private readonly SemaphoreSlim _seedGate = new(1, 1);

    /// <summary>
    /// Cara de LECTURA del motor de matrícula para el panel del instructor. Opcional
    /// (null = métricas en cero).
    /// </summary>
    /// <remarks>
    /// <b>Property injection por lo mismo que en el stub</b>: el motor de matrícula recibe el
    /// catálogo en su ctor, así que enchufarlo aquí desde el ctor sería un ciclo. Y por eso el
    /// composer tiene que inyectarlo sobre la instancia REGISTRADA, no sobre el stub por su
    /// nombre — inyectarlo en la otra deja el panel del instructor en 0 alumnos y $0, en
    /// silencio y con los tests en verde. Hay gate.
    /// </remarks>
    public IEnrollmentMetrics? EnrollmentMetrics { get; set; }

    /// <summary>Cache del mapping durable. Se releé cuando aparece una lección sin sembrar.</summary>
    private Dictionary<string, SeededLesson>? _seeded;

    public CatalogCourseCatalogProvider(
        ICatalogSource<AuthoredCourse> source,
        IJsonEntityStore store,
        IContentStream contentStream)
        : this(source, store, contentStream,
            new InMemoryCatalogIndex<CourseSummary>(StubCourseCatalogProvider.Descriptor, CatalogSettings.Unpaged))
    {
    }

    /// <param name="now">
    /// Reloj de la publicación desde el panel. Los tests lo fijan para poder afirmar la fecha
    /// sin depender del día en que corran.
    /// </param>
    internal CatalogCourseCatalogProvider(
        ICatalogSource<AuthoredCourse> source,
        IJsonEntityStore store,
        IContentStream contentStream,
        ICatalogIndex<CourseSummary> index,
        Func<DateTimeOffset>? now = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contentStream = contentStream ?? throw new ArgumentNullException(nameof(contentStream));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    private readonly Func<DateTimeOffset> _now;

    public async Task<CourseSearchResult> SearchAsync(CourseQuery query, CancellationToken cancellationToken = default)
    {
        var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
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

        // El MISMO motor y el MISMO descriptor que el stub. La búsqueda no puede comportarse
        // distinto según de dónde salgan los cursos: sería una regresión invisible que sólo
        // aparece al mover el flag a 'cms'.
        var result = _index.Search(
            catalog.Select(c => c.Course).ToList(),
            new CatalogQuery(Text: query.Text, Filters: filters.Count > 0 ? filters : null, Take: int.MaxValue));

        return new CourseSearchResult(result.Items, result.Total);
    }

    public async Task<CourseDetail?> GetCourseAsync(string courseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return null;
        }

        var id = courseId.Trim();
        var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.FirstOrDefault(c => string.Equals(c.Course.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<InstructorCoursesResult> GetForInstructorAsync(
        string instructorId,
        CancellationToken cancellationToken = default)
    {
        var currency = AcademyDemoSeed.Currency;
        if (string.IsNullOrWhiteSpace(instructorId))
        {
            return new InstructorCoursesResult(instructorId ?? string.Empty, Array.Empty<InstructorCourse>(), 0, 0m, currency);
        }

        var id = instructorId.Trim();
        var owned = (await LoadCatalogAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => string.Equals(c.Instructor.Id, id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Course.Rating)
            .ThenBy(c => c.Course.Title, StringComparer.Ordinal)
            .ToList();

        var rows = new List<InstructorCourse>(owned.Count);
        var totalStudents = 0;
        var totalRevenue = 0m;
        foreach (var course in owned)
        {
            var stats = EnrollmentMetrics is not null
                ? await EnrollmentMetrics.GetCourseStatsAsync(course.Course.Id, cancellationToken).ConfigureAwait(false)
                : new CourseEnrollmentStats(0, 0m);

            rows.Add(new InstructorCourse(
                Course: course.Course,
                Metrics: new CourseMetrics(stats.Students, stats.Revenue, currency, course.Course.Rating)));
            totalStudents += stats.Students;
            totalRevenue += stats.Revenue;
        }

        return new InstructorCoursesResult(id, rows, totalStudents, totalRevenue, currency);
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

        var currency = AcademyDemoSeed.Currency;
        var courseId = ResolvePublishedId(draft);
        var instructorId = string.IsNullOrWhiteSpace(draft.InstructorId) ? "ins-desconocido" : draft.InstructorId.Trim();
        var price = Math.Max(0m, draft.Price);

        var modules = new List<AuthoredModule>(draft.Modules.Count);
        var moduleOrder = 1;
        foreach (var m in draft.Modules)
        {
            var moduleId = $"{courseId}-m{moduleOrder}";
            var lessons = new List<AuthoredLesson>(m.Lessons.Count);
            var lessonOrder = 1;
            foreach (var l in m.Lessons)
            {
                if (l is null || string.IsNullOrWhiteSpace(l.Title))
                {
                    throw new ArgumentException("Cada lección requiere un título.", nameof(draft));
                }

                var lessonTitle = l.Title.Trim();
                lessons.Add(new AuthoredLesson(
                    Id: $"{moduleId}-l{lessonOrder}",
                    Title: lessonTitle,
                    Order: lessonOrder,
                    DurationMinutes: Math.Max(0, l.DurationMinutes),
                    VideoRef: l.VideoUrl,
                    Body: string.IsNullOrWhiteSpace(l.ContentBody) ? $"Lección: {lessonTitle}" : l.ContentBody.Trim(),
                    Resources: Array.Empty<CourseResource>(),
                    IsPreview: lessonOrder == 1 && moduleOrder == 1));
                lessonOrder++;
            }

            modules.Add(new AuthoredModule(
                Id: moduleId,
                Title: string.IsNullOrWhiteSpace(m.Title) ? $"Módulo {moduleOrder}" : m.Title.Trim(),
                Order: moduleOrder,
                Lessons: lessons));
            moduleOrder++;
        }

        var title = draft.Title.Trim();
        var summary = new CourseSummary(
            Id: courseId,
            Title: title,
            Summary: string.IsNullOrWhiteSpace(draft.Summary) ? title : draft.Summary.Trim(),
            Category: string.IsNullOrWhiteSpace(draft.Category) ? "General" : draft.Category.Trim(),
            Level: string.IsNullOrWhiteSpace(draft.Level) ? "Principiante" : draft.Level.Trim(),
            InstructorName: instructorId,
            CoverImageUrl: draft.CoverImageUrl,
            Price: price,
            Currency: currency,
            IsFree: price <= 0m,
            Rating: 0d, // sin reseñas todavía
            LessonCount: modules.Sum(m => m.Lessons.Count),
            DurationMinutes: modules.Sum(m => m.Lessons.Sum(l => l.DurationMinutes)),
            // Publicar ES el acto que fecha el curso, y la fecha viaja DENTRO del documento
            // que se escribe: sobrevive al reinicio sin un almacén aparte. Los cursos que ya
            // estaban en el overlay antes de este campo se releen sin él y quedan en «no
            // consta» — es la verdad sobre ellos, y no hay de dónde sacarla (#102).
            Status: CourseStatuses.Published,
            PublishedAt: DateOnly.FromDateTime(_now().UtcDateTime));

        var authored = new AuthoredCourse(
            new CourseDetail(
                Course: summary,
                Description: string.IsNullOrWhiteSpace(draft.Description) ? string.Empty : draft.Description.Trim(),
                Outcomes: draft.Outcomes ?? Array.Empty<string>(),
                Instructor: new CourseInstructor(instructorId, instructorId, string.Empty, string.Empty, null),
                Modules: Array.Empty<CourseModule>(),
                Plans: CoursePricingRules.Build(price, currency)),
            modules);

        var json = JsonSerializer.Serialize(authored, JsonOptions);
        await _store.WriteAsync(ResourceType, courseId, json, cancellationToken).ConfigureAwait(false);

        // Se devuelve YA SEMBRADO: quien publica abre su curso a continuación, y un detalle con
        // lecciones sin cuerpo es exactamente lo que el ContentItemId existe para evitar.
        return await MaterializeAsync(authored, cancellationToken).ConfigureAwait(false);
    }

    // ── Las dos capas ──────────────────────────────────────────────────

    /// <summary>
    /// El catálogo completo, ya sembrado: lo publicado por instructores gana sobre el contenido.
    /// </summary>
    private async Task<IReadOnlyList<CourseDetail>> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var published = await ReadPublishedAsync(cancellationToken).ConfigureAwait(false);
        var content = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        var merged = new List<AuthoredCourse>(published);
        if (published.Count == 0)
        {
            merged.AddRange(content);
        }
        else
        {
            var shadowed = new HashSet<string>(published.Select(c => c.Detail.Course.Id), StringComparer.OrdinalIgnoreCase);
            merged.AddRange(content.Where(c => !shadowed.Contains(c.Detail.Course.Id)));
        }

        var details = new List<CourseDetail>(merged.Count);
        foreach (var course in merged)
        {
            details.Add(await MaterializeAsync(course, cancellationToken).ConfigureAwait(false));
        }

        return details;
    }

    /// <summary>
    /// Los cursos publicados por instructores. Una entrada ilegible se SALTA.
    /// </summary>
    /// <remarks>
    /// Un JSON corrupto en el store —un despliegue a medias, una edición a mano— no puede
    /// tumbar el catálogo entero: se pierde ese curso, no la escuela. Es la misma postura que
    /// toma la fuente de contenido al omitir un <c>coursePage</c> incompleto.
    /// </remarks>
    private async Task<IReadOnlyList<AuthoredCourse>> ReadPublishedAsync(CancellationToken cancellationToken)
    {
        // ListAsync devuelve los DOCUMENTOS, no las claves.
        var documents = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            return Array.Empty<AuthoredCourse>();
        }

        var courses = new List<AuthoredCourse>(documents.Count);
        foreach (var json in documents)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            AuthoredCourse? course;
            try
            {
                course = JsonSerializer.Deserialize<AuthoredCourse>(json, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (course?.Detail?.Course is not null && !string.IsNullOrWhiteSpace(course.Detail.Course.Id))
            {
                courses.Add(course);
            }
        }

        return courses;
    }

    // ── Siembra durable de lecciones en el IContentStream (Kind=lesson) ──

    /// <summary>
    /// El curso con su currículum resuelto: cada lección con el id del item del feed que lleva
    /// su cuerpo, sembrándolo si hace falta.
    /// </summary>
    private async Task<CourseDetail> MaterializeAsync(AuthoredCourse course, CancellationToken cancellationToken)
    {
        var modules = new List<CourseModule>(course.Modules.Count);
        foreach (var module in course.Modules.OrderBy(m => m.Order))
        {
            var lessons = new List<CourseLesson>(module.Lessons.Count);
            foreach (var lesson in module.Lessons.OrderBy(l => l.Order))
            {
                var contentItemId = await ResolveContentItemAsync(
                    course.Detail.Instructor.Id, lesson, cancellationToken).ConfigureAwait(false);

                lessons.Add(new CourseLesson(
                    Id: lesson.Id,
                    Title: lesson.Title,
                    Order: lesson.Order,
                    DurationMinutes: lesson.DurationMinutes,
                    VideoRef: lesson.VideoRef,
                    ContentItemId: contentItemId,
                    Resources: lesson.Resources,
                    IsPreview: lesson.IsPreview));
            }

            modules.Add(new CourseModule(module.Id, module.Title, module.Order, lessons));
        }

        return course.Detail with { Modules = modules };
    }

    /// <summary>
    /// El item del feed que lleva el cuerpo de esta lección, sembrándolo la primera vez y
    /// cuando el editor lo cambió.
    /// </summary>
    /// <remarks>
    /// <b>La huella del cuerpo es lo que hace que editar se vea</b>, y es la decisión fina de
    /// esta clase. <see cref="IContentStream"/> sólo sabe CREAR —no hay update—, así que un
    /// mapping por <c>lessonId</c> a secas serviría para siempre el primer cuerpo: el editor
    /// corrige una lección, guarda, y el alumno sigue leyendo la versión vieja sin que nada
    /// falle. Con la huella dentro de la clave de comparación, un cuerpo distinto siembra un
    /// item nuevo y el player lo ve.
    ///
    /// <para><b>El item viejo queda en el feed y eso es deliberado</b>, no un descuido: es
    /// contenido que alguien pudo haber enlazado, y borrarlo exigiría un delete que el seam no
    /// tiene. El coste es un item huérfano por edición; la alternativa era «editas y no se
    /// ve», que este repo ya nombra como el peor de los dos.</para>
    ///
    /// <para><b>El <c>lessonId</c> NO cambia al reescribir el cuerpo</b>, y por eso el progreso
    /// del alumno sobrevive: <c>CourseProgress.CompletedLessonIds</c> guarda el id de la
    /// lección, no el del item.</para>
    /// </remarks>
    private async Task<string> ResolveContentItemAsync(
        string authorId,
        AuthoredLesson lesson,
        CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(lesson.Body);

        var seeded = _seeded ?? await LoadSeededAsync(cancellationToken).ConfigureAwait(false);
        if (seeded.TryGetValue(lesson.Id, out var hit) && string.Equals(hit.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return hit.ContentItemId;
        }

        await _seedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Doble comprobación: entre el miss y el lock, otro request pudo sembrarla. Sin
            // esto, dos búsquedas simultáneas sobre un curso recién autorado crean dos items.
            seeded = await LoadSeededAsync(cancellationToken).ConfigureAwait(false);
            if (seeded.TryGetValue(lesson.Id, out hit) && string.Equals(hit.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return hit.ContentItemId;
            }

            var item = await _contentStream.CreateAsync(
                new NewContentItem(
                    AuthorId: authorId,
                    Body: lesson.Body,
                    MediaUrl: lesson.VideoRef,
                    // El MISMO feed que usa Blogs, con Kind=lesson — polimorfismo, no
                    // instanciación (DIP, ADR 0002).
                    Kind: "lesson"),
                cancellationToken).ConfigureAwait(false);

            var record = new SeededLesson(lesson.Id, item.Id, fingerprint);
            await _store.WriteAsync(
                LessonContentResourceType,
                lesson.Id,
                JsonSerializer.Serialize(record, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            var updated = new Dictionary<string, SeededLesson>(seeded, StringComparer.OrdinalIgnoreCase)
            {
                [lesson.Id] = record,
            };
            _seeded = updated;

            return item.Id;
        }
        finally
        {
            _seedGate.Release();
        }
    }

    private async Task<Dictionary<string, SeededLesson>> LoadSeededAsync(CancellationToken cancellationToken)
    {
        var documents = await _store.ListAsync(LessonContentResourceType, cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<string, SeededLesson>(documents.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var json in documents)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            SeededLesson? record;
            try
            {
                record = JsonSerializer.Deserialize<SeededLesson>(json, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (record is not null
                && !string.IsNullOrWhiteSpace(record.LessonId)
                && !string.IsNullOrWhiteSpace(record.ContentItemId))
            {
                map[record.LessonId] = record;
            }
        }

        _seeded = map;
        return map;
    }

    /// <summary>
    /// Huella del cuerpo. SHA-256 recortado: no es un secreto, sólo tiene que cambiar cuando el
    /// texto cambie y caber en un JSON sin estorbar.
    /// </summary>
    private static string Fingerprint(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty)))[..16].ToLowerInvariant();

    private static string ResolvePublishedId(CourseDraft draft)
    {
        var fromTitle = Sanitize(draft.Title);
        // El sufijo aleatorio y no un contador: el contador vivía en el proceso y este catálogo
        // es durable, así que tras un reinicio volvería a 1 y el segundo curso publicado
        // pisaría al primero.
        return fromTitle.Length > 0
            ? $"{fromTitle}-{Guid.NewGuid().ToString("n")[..6]}"
            : GeneratedIdPrefix + Guid.NewGuid().ToString("n")[..12];
    }

    /// <summary>
    /// Un id apto para ser clave del store: minúsculas, dígitos y guiones, nada más.
    /// </summary>
    /// <remarks>
    /// La clave termina siendo un nombre de archivo en el adapter FileSystem, así que un id con
    /// <c>../</c> no es un id feo: es una escritura fuera del directorio del recurso. Lista
    /// BLANCA, porque una negra siempre se queda corta.
    /// </remarks>
    private static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(raw.Length, MaxKeyLength));
        foreach (var c in raw.Trim().ToLowerInvariant())
        {
            if (builder.Length == MaxKeyLength)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if ((c == '-' || c == '_' || c == ' ') && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>Lo que se recuerda de una lección ya sembrada.</summary>
    private sealed record SeededLesson(string LessonId, string ContentItemId, string Fingerprint);
}
