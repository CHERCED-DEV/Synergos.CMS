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
/// Proyección liviana de un curso para el catálogo/grid (card): metadatos +
/// precio + instructor + agregados (rating, nº lecciones, duración). El detalle
/// rico (módulos + lecciones + planes) vive en <see cref="CourseDetail"/>.
/// </summary>
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
    int DurationMinutes);

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
}
