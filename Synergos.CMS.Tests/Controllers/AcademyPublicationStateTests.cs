using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Lo que el borde de Educación dice sobre el ESTADO de un curso y sobre CUÁNDO se publicó
/// (#102): la píldora de la consola del instructor y el desplegable «Más recientes».
/// </summary>
/// <remarks>
/// <b>Los dos defectos eran el mismo dato faltante y ninguno fallaba a la vista.</b> El
/// borde no emitía <c>status</c>, y el normalizador del otro lado degrada a
/// <c>published</c> cuando la clave no está: la consola afirmaba «Publicado» de todo y el
/// KPI contaba «N publicados / N en total» pasara lo que pasara. Y <c>sort=newest</c> era
/// una opción MUERTA — el desplegable la ofrecía, el valor llegaba al controller, cruzaba
/// el gate de claves y caía al caso por defecto—, así que contra el mock reordenaba y
/// contra el servidor real no hacía nada.
///
/// <para><b>Se afirma sobre el JSON serializado con <see cref="JsonSerializerDefaults.Web"/>
/// —lo que usa MVC— y no sobre las propiedades del DTO</b>: comparar propiedades de C# es
/// una tautología (afirma lo que el controller decidió poner) y además no ve el casing de
/// la clave, que es por donde se desvía un contrato.</para>
///
/// <para><b>El fixture tiene que EXIGIR la regla</b>: lleva un curso en borrador y uno
/// publicado, porque con los dos publicados emitir <c>status</c> o no da exactamente el
/// mismo resultado y el test no probaría nada. Que hoy ninguna de las tres fuentes
/// produzca un borrador no lo vuelve ficción: lo que se está vigilando es que el borde
/// PASE lo que dice el catálogo en vez de afirmarlo él, que es el defecto que había.</para>
/// </remarks>
public sealed class AcademyPublicationStateTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly ICourseCatalogProvider _catalog = Substitute.For<ICourseCatalogProvider>();
    private readonly IEnrollmentService _enrollments = Substitute.For<IEnrollmentService>();
    private readonly ICertificateService _certificates = Substitute.For<ICertificateService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IEnrollmentMetrics _metrics = Substitute.For<IEnrollmentMetrics>();

    public AcademyPublicationStateTests()
    {
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns("$ 0");
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns("elena@synergos.co");
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        _metrics.GetCourseStatsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CourseEnrollmentStats(0, 0m));
        _metrics.GetCourseRosterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CourseRosterEntry>());
    }

    private AcademyController BuildSut()
        => new(_catalog, _enrollments, _certificates, _priceFormatter, _gate, _metrics);

    private static JsonElement Json(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, Web);
    }

    private static CourseSummary Course(
        string id,
        string title,
        string status = CourseStatuses.Published,
        DateOnly? publishedAt = null) => new(
        Id: id, Title: title, Summary: "Resumen", Category: "Desarrollo", Level: "Intermedio",
        InstructorName: "Elena Restrepo", CoverImageUrl: null, Price: 180_000m, Currency: "COP",
        IsFree: false, Rating: 4.5, LessonCount: 4, DurationMinutes: 120,
        Status: status, PublishedAt: publishedAt);

    private void CatalogoDelInstructor(params CourseSummary[] courses)
        => _catalog.GetForInstructorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InstructorCoursesResult(
                "elena@synergos.co",
                courses.Select(c => new InstructorCourse(c, new CourseMetrics(0, 0m, "COP", c.Rating))).ToList(),
                0, 0m, "COP"));

    private void CatalogoPublico(params CourseSummary[] courses)
        => _catalog.SearchAsync(Arg.Any<CourseQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CourseSearchResult(courses, courses.Length));

    private static List<string> Rows(JsonElement body, string key)
        => body.GetProperty("courses").EnumerateArray()
            .Select(c => c.GetProperty(key).GetString() ?? "(null)")
            .ToList();

    // ── La píldora «Estado» y el KPI «Cursos publicados» ────────────────────────

    /// <summary>
    /// La consola PASA el estado que dice el catálogo, curso por curso.
    /// </summary>
    /// <remarks>
    /// La mutación que lo pone en rojo es escribir <c>Status: CourseStatuses.Published</c> en
    /// el mapeo del controller — que es exactamente lo que el borde hacía de hecho al no
    /// emitir la clave, sólo que delegado en el valor por defecto del otro lado.
    /// </remarks>
    [Fact]
    public async Task LaConsola_PasaElEstadoDeCadaCurso()
    {
        CatalogoDelInstructor(
            Course("borrador", "RxJS avanzado", CourseStatuses.Draft),
            Course("vivo", "Arquitectura limpia"));

        var body = Json(await BuildSut().InstructorCourses(default));

        Assert.Equal(new[] { CourseStatuses.Draft, CourseStatuses.Published }, Rows(body, "status"));
    }

    /// <summary>
    /// «Cursos publicados» cuenta los publicados, no las filas.
    /// </summary>
    /// <remarks>
    /// El KPI lo calcula el otro lado filtrando por <c>status === 'published'</c>, así que
    /// esto afirma lo único que este lado controla: que de dos cursos salga UNO publicado.
    /// Con la clave ausente salían dos, y el panel decía «2 publicados / 2 en total» sobre un
    /// portafolio con un borrador dentro.
    /// </remarks>
    [Fact]
    public async Task LaConsola_NoDaPorPublicadoLoQueNoLoEsta()
    {
        CatalogoDelInstructor(
            Course("borrador", "RxJS avanzado", CourseStatuses.Draft),
            Course("vivo", "Arquitectura limpia"));

        var body = Json(await BuildSut().InstructorCourses(default));

        Assert.Equal(1, Rows(body, "status").Count(s => s == CourseStatuses.Published));
    }

    /// <summary>
    /// La fecha viaja como <c>yyyy-MM-dd</c>, y la que no consta viaja como nula.
    /// </summary>
    /// <remarks>
    /// Nulo NO es una fecha vacía inventada ni el día de hoy: son los cursos que ya estaban en
    /// el overlay durable antes de que el campo existiera, y de ésos no se sabe. El
    /// normalizador del otro lado lo lee como cadena vacía, que es lo que significa.
    /// </remarks>
    [Fact]
    public async Task LaConsola_EmiteLaFechaDePublicacionYElNoConsta()
    {
        CatalogoDelInstructor(
            Course("fechado", "Arquitectura limpia", publishedAt: new DateOnly(2026, 2, 17)),
            Course("sin-fecha", "Curso viejo del overlay"));

        var body = Json(await BuildSut().InstructorCourses(default));

        var courses = body.GetProperty("courses");
        Assert.Equal("2026-02-17", courses[0].GetProperty("publishedAt").GetString());
        Assert.Equal(JsonValueKind.Null, courses[1].GetProperty("publishedAt").ValueKind);
    }

    // ── El desplegable «Más recientes» ──────────────────────────────────────────

    /// <summary>
    /// <c>sort=newest</c> ordena por fecha de publicación, de la más nueva a la más vieja.
    /// </summary>
    /// <remarks>
    /// El fixture llega DESORDENADO y con las fechas cruzadas respecto al título y al precio,
    /// para que el orden correcto no se pueda obtener por accidente: ordenar por cualquier
    /// otro criterio —incluido «no ordenar»— da otra respuesta.
    /// </remarks>
    [Fact]
    public async Task Courses_Newest_OrdenaPorFechaDePublicacion()
    {
        CatalogoPublico(
            Course("b", "B — medio", publishedAt: new DateOnly(2026, 4, 3)),
            Course("a", "A — el más viejo", publishedAt: new DateOnly(2025, 11, 24)),
            Course("c", "C — el más nuevo", publishedAt: new DateOnly(2026, 6, 11)));

        var body = Json(await BuildSut().Courses(null, null, null, null, "newest", default));

        Assert.Equal(new[] { "c", "b", "a" }, Rows(body, "id"));
    }

    /// <summary>
    /// Un curso sin fecha va al FINAL de «Más recientes», no al principio.
    /// </summary>
    /// <remarks>
    /// Sale de ordenar descendente sobre un <c>DateOnly?</c>, pero se afirma porque es una
    /// decisión: «no consta» no puede encabezar una lista que promete lo más reciente.
    /// </remarks>
    [Fact]
    public async Task Courses_Newest_DejaAlFinalLoQueNoTieneFecha()
    {
        CatalogoPublico(
            Course("sin-fecha", "A — sin fecha"),
            Course("con-fecha", "B — de 2025", publishedAt: new DateOnly(2025, 11, 24)));

        var body = Json(await BuildSut().Courses(null, null, null, null, "newest", default));

        Assert.Equal(new[] { "con-fecha", "sin-fecha" }, Rows(body, "id"));
    }

    /// <summary>
    /// Un <c>sort</c> que este borde no conoce NO reordena ni vacía el catálogo.
    /// </summary>
    /// <remarks>
    /// Es el caso por defecto, y sigue siendo el correcto: un vocabulario que se amplíe en la
    /// UI antes que aquí tiene que enseñar el catálogo entero en el orden del motor, no
    /// ninguno. Lo que cambió con #102 es que <c>newest</c> dejó de caer aquí.
    /// </remarks>
    [Fact]
    public async Task Courses_SortDesconocido_NoReordena()
    {
        CatalogoPublico(
            Course("b", "B", publishedAt: new DateOnly(2026, 4, 3)),
            Course("a", "A", publishedAt: new DateOnly(2026, 6, 11)));

        var body = Json(await BuildSut().Courses(null, null, null, null, "popularidad", default));

        Assert.Equal(new[] { "b", "a" }, Rows(body, "id"));
    }
}
