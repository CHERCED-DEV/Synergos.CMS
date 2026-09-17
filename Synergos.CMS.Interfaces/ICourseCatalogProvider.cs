namespace Synergos.CMS.Interfaces;

/// <summary>
/// Filtros de la búsqueda del catálogo de cursos (dominio Educación — LMS).
/// Todos opcionales: el catálogo entero si todos son <c>null</c>. Calca
/// <c>ProductQuery</c> (Tienda) / <c>AvailabilityQuery</c> (Hoteles) — la
/// pieza del MOTOR que resuelve "qué cursos hay" para el catálogo buscable.
/// </summary>
public sealed record CourseQuery(
    string? Text = null,
    string? Category = null,
    string? Level = null);

/// <summary>
/// Resultado de <see cref="ICourseCatalogProvider.SearchAsync"/>: los cursos
/// que matchean + el total. Forma estable que el módulo Angular
/// <c>course-catalog</c> consume para el grid + filtros.
/// </summary>
public sealed record CourseSearchResult(
    IReadOnlyList<CourseSummary> Courses,
    int Total);

/// <summary>
/// El vocabulario de estado de un curso. Es el de la UI —<c>published | draft | review</c>—
/// y no uno propio, para que nadie tenga que traducirlo por el camino (ADR 0083).
/// </summary>
/// <remarks>
/// <b>Hoy el motor sólo produce <see cref="Published"/>, y eso es un hecho, no un atajo</b>
/// (#102): las tres fuentes del catálogo sólo pueden servir cursos publicados —el caché de
/// Umbraco sólo contiene contenido publicado, el seed ES el catálogo de demo, y el overlay
/// durable se escribe en el acto de publicar—. Los otros dos existen porque son lo que la
/// consola del instructor sabe pintar y porque el día que alguien los produzca no puede
/// además tener que inventarse el nombre.
///
/// <para><b>Disparador para que dejen de estar sin productor:</b> que la consola del
/// instructor tenga que listar lo NO publicado. Hoy no puede — el catálogo mira el caché
/// publicado de Umbraco, y lo que un editor guardó sin publicar no llega a él.</para>
/// </remarks>
public static class CourseStatuses
{
    /// <summary>Publicado: se anuncia en el catálogo y se puede cursar.</summary>
    public const string Published = "published";

    /// <summary>Borrador: lo escribió su autor y todavía no se anuncia.</summary>
    public const string Draft = "draft";

    /// <summary>En revisión: enviado a aprobación editorial.</summary>
    public const string Review = "review";
}

/// <summary>
/// Proyección liviana de un curso para el catálogo/grid (card): metadatos +
/// precio + instructor + agregados (rating, nº lecciones, duración). El detalle
/// rico (módulos + lecciones + planes) vive en <see cref="CourseDetail"/>.
/// </summary>
/// <remarks>
/// <b><see cref="Status"/> y <see cref="PublishedAt"/> son trabajo de SEAM y no de mapeo</b>
/// (#102). La consola del instructor pinta la píldora «Estado» y cuenta «Cursos publicados»,
/// y el borde no emitía ninguno de los dos: el normalizador del otro lado degrada a
/// <c>published</c> cuando la clave falta, así que la consola afirmaba que todo está
/// publicado sin que nadie se lo hubiera dicho. Y el desplegable «Más recientes» no tenía
/// con qué ordenar.
///
/// <para><b>El default de <see cref="Status"/> es <c>published</c> y NO «no consta»</b>, al
/// revés que el de <see cref="PublishedAt"/>, y la diferencia es que aquí sí se sabe: un
/// curso que el catálogo sirve está publicado, porque eso es lo que el catálogo es. La
/// fuente que algún día sepa servir un borrador lo declara; el vocabulario y el disparador
/// están en <see cref="CourseStatuses"/>.</para>
///
/// <para><b><see cref="PublishedAt"/> es nullable porque hay cursos de los que no se
/// sabe</b>: los que ya estaban en el overlay durable antes de que este campo existiera no
/// llevan fecha, y no hay de dónde sacarla. Nulo dice eso —no consta— y no se rellena
/// derivándola del id ni del orden del seed, que daría un orden estable y falso. Quien
/// ordena por «más recientes» los pone al final.</para>
///
/// <para><b>Es <see cref="DateOnly"/> a propósito.</b> La fuente de contenido sólo tiene la
/// fecha de la última publicación del nodo, sin zona horaria fiable, así que fingir hora y
/// desfase sería precisión inventada. Los empates los rompe el título, como el resto de los
/// órdenes de este catálogo.</para>
/// </remarks>
public sealed record CourseSummary(
    string Id,
    string Title,
    string Summary,
    string Category,
    string Level,
    string InstructorName,
    string? CoverImageUrl,
    decimal Price,
    string Currency,
    bool IsFree,
    double Rating,
    int LessonCount,
    int DurationMinutes,
    string Status = CourseStatuses.Published,
    DateOnly? PublishedAt = null);

/// <summary>
/// Detalle completo de un curso (la PDP-curso): el resumen + descripción +
/// instructor + currículum (módulos → lecciones) + planes de precio. Lo
/// devuelve <see cref="ICourseCatalogProvider.GetCourseAsync"/>.
/// </summary>
public sealed record CourseDetail(
    CourseSummary Course,
    string Description,
    IReadOnlyList<string> Outcomes,
    CourseInstructor Instructor,
    IReadOnlyList<CourseModule> Modules,
    IReadOnlyList<CoursePricingPlan> Plans);

/// <summary>Perfil del instructor (reusa el shape de autor de Blogs — avatar/bio).</summary>
public sealed record CourseInstructor(
    string Id,
    string Name,
    string Headline,
    string Bio,
    string? AvatarUrl);

/// <summary>
/// Una sección/módulo del currículum: título + orden + sus lecciones. Calca
/// <c>course → section → lesson</c> de Udemy/Coursera/Moodle (app-spec §1).
/// </summary>
public sealed record CourseModule(
    string Id,
    string Title,
    int Order,
    IReadOnlyList<CourseLesson> Lessons);

/// <summary>
/// Una lección del currículum. <see cref="ContentItemId"/> es la CLAVE DEL
/// POLIMORFISMO: apunta al item del <see cref="IContentStream"/> (Kind=lesson)
/// del que sale el contenido editorial de la lección (cuerpo/transcripción +
/// autor). El catálogo NO duplica ese contenido — lo referencia. El resto
/// (videoRef, recursos, orden, duración, gratis/preview) es metadato propio
/// del LMS. (app-spec §4 — Educación reusa el feed de Blogs por polimorfismo.)
/// </summary>
public sealed record CourseLesson(
    string Id,
    string Title,
    int Order,
    int DurationMinutes,
    string? VideoRef,
    string ContentItemId,
    IReadOnlyList<CourseResource> Resources,
    bool IsPreview = false);

/// <summary>Un recurso descargable adjunto a una lección (PDF/dataset/etc.).</summary>
public sealed record CourseResource(string Title, string Url, string Kind);

/// <summary>
/// Borrador de un curso que un instructor PUBLICA desde el panel (curriculum
/// builder). Es el input de <see cref="ICourseCatalogProvider.PublishCourseAsync"/>:
/// metadatos + precio + currículum (módulos → lecciones). El proveedor le asigna
/// id/slug estables, siembra las lecciones al feed (IContentStream Kind=lesson) y
/// lo agrega al catálogo. Calca <c>EventDraft</c> (Eventos) / <c>CourseDetail</c>.
/// </summary>
public sealed record CourseDraft(
    string Title,
    string Summary,
    string Description,
    string School,
    string Category,
    string Level,
    string InstructorId,
    decimal Price,
    IReadOnlyList<CourseDraftModule> Modules,
    IReadOnlyList<string>? Outcomes = null,
    string? CoverImageUrl = null);

/// <summary>Un módulo del borrador: título + sus lecciones (orden = posición en la lista).</summary>
public sealed record CourseDraftModule(
    string Title,
    IReadOnlyList<CourseDraftLesson> Lessons);

/// <summary>Una lección del borrador: título + video + duración (+ cuerpo editorial opcional).</summary>
public sealed record CourseDraftLesson(
    string Title,
    string? VideoUrl,
    int DurationMinutes,
    string? ContentBody = null);

/// <summary>
/// Métricas agregadas de un curso para el panel del instructor (performance +
/// revenue): matriculados, ingresos acumulados y rating. Las alimenta el motor de
/// matrícula (<c>IEnrollmentService</c>) por composición — el catálogo no las duplica.
/// </summary>
public sealed record CourseMetrics(
    int Students,
    decimal Revenue,
    string Currency,
    double Rating);

/// <summary>
/// Un curso del instructor con sus métricas (la fila del dashboard del panel).
/// </summary>
public sealed record InstructorCourse(
    CourseSummary Course,
    CourseMetrics Metrics);

/// <summary>
/// Resultado de <see cref="ICourseCatalogProvider.GetForInstructorAsync"/>: los
/// cursos del instructor con métricas + los totales del panel (alumnos e ingresos).
/// </summary>
public sealed record InstructorCoursesResult(
    string InstructorId,
    IReadOnlyList<InstructorCourse> Courses,
    int TotalStudents,
    decimal TotalRevenue,
    string Currency);

/// <summary>
/// Un plan de precio de inscripción: contado o N cuotas (EMI). El motor cobra
/// <see cref="Total"/>; <see cref="Installments"/> es informativo para la UI
/// (app-spec §4 — cuotas/EMI estándar en cursos de precio alto).
/// </summary>
public sealed record CoursePricingPlan(
    string Code,
    string Label,
    decimal Total,
    string Currency,
    int Installments);

/// <summary>
/// Catálogo de cursos del dominio Educación (LMS) — la pieza del MOTOR que
/// resuelve la búsqueda buscable/filtrable del catálogo y el detalle de un
/// curso (currículum + instructor + planes). Es el equivalente educativo del
/// <see cref="IProductCatalogProvider"/> (Tienda) o el
/// <see cref="IRoomAvailabilityProvider"/> (Hoteles).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reusa Blogs por POLIMORFISMO, no por instanciación.</b> El contenido
/// editorial de cada lección (cuerpo/transcripción + autor) NO se duplica en
/// el catálogo: la <see cref="CourseLesson.ContentItemId"/> referencia un item
/// del <see cref="IContentStream"/> con <c>Kind=lesson</c>. El stub del catálogo
/// SIEMBRA esas lecciones en el stream vía <c>IContentStream</c> (DIP, ADR 0002)
/// — depende de la abstracción de feed, no del módulo Blogs ni de su schema.
/// </para>
/// <para>
/// Seam stub-first (igual que el resto del motor): el default
/// <c>StubCourseCatalogProvider</c> (Application, lógica pura/determinista)
/// sirve un catálogo sembrado en memoria (varias categorías × cursos × módulos
/// × lecciones) para que la demo corra end-to-end; el adapter real (Examine
/// sobre <c>coursePage</c> / store LMS) implementa la misma seam y se registra
/// vía el composer sin tocar el módulo Angular ni el controller. ADR 0002
/// (Application sin Umbraco) + ADR 0075 (seam con tests).
/// </para>
/// </remarks>
public interface ICourseCatalogProvider
{
    /// <summary>
    /// Busca cursos por texto/categoría/nivel y devuelve las cards + el total.
    /// Nunca lanza por filtro vacío: sin matches devuelve <c>Courses = []</c>.
    /// </summary>
    Task<CourseSearchResult> SearchAsync(CourseQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el detalle de un curso (currículum + instructor + planes), o
    /// <c>null</c> si no existe. Las lecciones referencian el contenido editorial
    /// vía <see cref="CourseLesson.ContentItemId"/> (IContentStream Kind=lesson).
    /// </summary>
    Task<CourseDetail?> GetCourseAsync(string courseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los cursos publicados por un instructor con sus métricas
    /// (matriculados, ingresos, rating) + los totales del panel. Es la cara de
    /// AUTOR del LMS (performance/revenue). Nunca lanza por instructor sin cursos:
    /// devuelve <c>Courses = []</c> + totales en cero.
    /// </summary>
    Task<InstructorCoursesResult> GetForInstructorAsync(string instructorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica un curso nuevo (creado por un instructor) en el catálogo: desde ese
    /// momento aparece en <see cref="SearchAsync"/> y su detalle se resuelve en
    /// <see cref="GetCourseAsync"/>. Siembra las lecciones al feed
    /// (<see cref="IContentStream"/> Kind=lesson) igual que el catálogo sembrado
    /// (polimorfismo Blogs). Devuelve el detalle publicado (con id/slug asignados).
    /// Valida ≥1 módulo. El adapter real escribe a <c>IContentService</c>/índice; el
    /// stub lo agrega al catálogo en memoria. Es la seam que consume el panel del
    /// instructor — la cara de autor NO conoce el almacenamiento del catálogo (DIP).
    /// </summary>
    Task<CourseDetail> PublishCourseAsync(CourseDraft draft, CancellationToken cancellationToken = default);
}
