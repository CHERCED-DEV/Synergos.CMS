using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Que la consola del instructor de verdad LISTE a sus alumnos (#107) — el cableado, no la forma.
/// </summary>
/// <remarks>
/// <para><b>La forma del DTO ya la vigila <see cref="AcademyContractShapeTests"/> y no alcanza.</b>
/// Un <c>record</c> con los campos correctos y una acción que nunca lo llena pasa ese test en
/// verde y deja la pestaña «Alumnos» vacía contra un servidor lleno. Es la lección de la HU #14
/// rebanada 5: el gate tiene que mirar lo que hay ENTRE el seam y el borde.</para>
///
/// <para>Y por eso el fixture trae DOS cursos con un alumno cada uno: con uno solo, una acción
/// que preguntara el roster una vez y lo repitiera daría el mismo resultado.</para>
/// </remarks>
public sealed class InstructorRosterWiringTests
{
    private const string Instructor = "elena@synergos.co";

    private readonly ICourseCatalogProvider _catalog = Substitute.For<ICourseCatalogProvider>();
    private readonly IEnrollmentService _enrollments = Substitute.For<IEnrollmentService>();
    private readonly ICertificateService _certificates = Substitute.For<ICertificateService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IEnrollmentMetrics _metrics = Substitute.For<IEnrollmentMetrics>();

    // Con la zona del sitio, como la resuelve el contenedor: el día de la inscripción es el de Bogotá.
    private AcademyController BuildSut()
        => new(_catalog, _enrollments, _certificates, _priceFormatter, _gate, _metrics,
            Options.Create(new ListadosSettings()));

    private static CourseSummary Course(string id, string title) => new(
        Id: id, Title: title, Summary: "Resumen", Category: "Datos", Level: "beginner",
        InstructorName: "Elena Restrepo", CoverImageUrl: null, Price: 180_000m, Currency: "COP",
        IsFree: false, Rating: 4.8, LessonCount: 10, DurationMinutes: 300);

    /// <summary>Quien mira es instructor y no admin: pasa la puerta de la consola, y «admin» a secas no.</summary>
    private void ComoInstructor()
        => _gate.HasAnyRole(Arg.Any<string?>()).Returns(c => !(c.Arg<string?>() ?? "").Split(',').SequenceEqual(["admin"]));

    /// <summary>Quien mira es admin: pasa la puerta de la consola y la de «admin».</summary>
    private void ComoAdmin()
        => _gate.HasAnyRole(Arg.Any<string?>()).Returns(c => (c.Arg<string?>() ?? "").Split(',').Contains("admin"));

    /// <summary>El catálogo entero —lo que ve un admin—, con los dos cursos de Elena y uno de otra instructora.</summary>
    private void CatalogoConTresCursos()
    {
        _catalog.SearchAsync(Arg.Any<CourseQuery>(), Arg.Any<CancellationToken>()).Returns(new CourseSearchResult(
            new[] { Course("sql", "SQL para analistas") with { Rating = 4.5 }, Course("excel", "Excel financiero"), Course("git", "Git desde cero") with { Rating = 4.9 } },
            3));
        _metrics.GetCourseStatsAsync("excel", Arg.Any<CancellationToken>()).Returns(new CourseEnrollmentStats(1, 180_000m));
        _metrics.GetCourseStatsAsync("sql", Arg.Any<CancellationToken>()).Returns(new CourseEnrollmentStats(1, 180_000m));
        _metrics.GetCourseStatsAsync("git", Arg.Any<CancellationToken>()).Returns(new CourseEnrollmentStats(1, 0m));
        _metrics.GetCourseRosterAsync("git", Arg.Any<CancellationToken>()).Returns(
            new[] { new CourseRosterEntry("caro-op", "Caro Díaz", "git", 0, new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero)) });
    }

    private void DosCursosConUnAlumnoCadaUno()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Instructor);
        ComoInstructor();
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string>()).Returns("$180.000");

        _catalog.GetForInstructorAsync(Instructor, Arg.Any<CancellationToken>()).Returns(
            new InstructorCoursesResult(
                Instructor,
                new[]
                {
                    new InstructorCourse(Course("excel", "Excel financiero"), new CourseMetrics(1, 180_000m, "COP", 4.8)),
                    new InstructorCourse(Course("sql", "SQL para analistas"), new CourseMetrics(1, 180_000m, "COP", 4.5)),
                },
                TotalStudents: 2, TotalRevenue: 360_000m, Currency: "COP"));

        _metrics.GetCourseRosterAsync("excel", Arg.Any<CancellationToken>()).Returns(
            new[] { new CourseRosterEntry("ana-op", "Ana Rincón", "excel", 40, new DateTimeOffset(2026, 6, 20, 15, 30, 0, TimeSpan.Zero)) });
        _metrics.GetCourseRosterAsync("sql", Arg.Any<CancellationToken>()).Returns(
            new[] { new CourseRosterEntry("beto-op", "Beto Salas", "sql", 10, new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero)) });
    }

    private async Task<AcademyController.InstructorCoursesResponse> Consola()
    {
        var result = await BuildSut().InstructorCourses(default);
        return Assert.IsType<AcademyController.InstructorCoursesResponse>(
            Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact] // el cableado: los alumnos de TODOS sus cursos llegan a la respuesta
    public async Task La_consola_lista_a_los_alumnos_de_cada_curso()
    {
        DosCursosConUnAlumnoCadaUno();

        var consola = await Consola();

        Assert.Equal(
            new[] { "Ana Rincón", "Beto Salas" },
            consola.Students.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
        await _metrics.Received(1).GetCourseRosterAsync("excel", Arg.Any<CancellationToken>());
        await _metrics.Received(1).GetCourseRosterAsync("sql", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// El título del curso lo pone el borde, y el de CADA fila es el suyo.
    /// </summary>
    /// <remarks>
    /// La columna «Curso» de la tabla lo pinta. Tomar el del primer curso —o el del último de la
    /// vuelta— daría una tabla entera plausible y equivocada: alumnos reales bajo el curso de
    /// otro.
    /// </remarks>
    [Fact]
    public async Task Cada_alumno_lleva_el_titulo_de_SU_curso()
    {
        DosCursosConUnAlumnoCadaUno();

        var consola = await Consola();

        Assert.Equal("Excel financiero", consola.Students.Single(s => s.CourseId == "excel").CourseTitle);
        Assert.Equal("SQL para analistas", consola.Students.Single(s => s.CourseId == "sql").CourseTitle);
    }

    /// <summary>
    /// La fecha de inscripción sale como <c>yyyy-MM-dd</c>, que es lo que la celda pinta crudo.
    /// </summary>
    /// <remarks>
    /// La columna «Inscrito» interpola el valor tal cual (<c>{{ row.enrolledAt }}</c>), sin el
    /// <c>formatDate</c> que sí recibe <c>lastActivityAt</c> en «mi aprendizaje». Un ISO completo
    /// sacaría la T y el desfase horario a la tabla.
    /// </remarks>
    [Fact]
    public async Task La_fecha_de_inscripcion_va_sin_hora()
    {
        DosCursosConUnAlumnoCadaUno();

        var consola = await Consola();

        Assert.Equal("2026-06-20", consola.Students.Single(s => s.CourseId == "excel").EnrolledAt);
    }

    [Fact] // el día de la inscripción es el del sitio: las 22:30 del 2 en Bogotá no son el 3.
    public async Task La_fecha_de_inscripcion_es_el_dia_del_sitio()
    {
        DosCursosConUnAlumnoCadaUno();
        _metrics.GetCourseRosterAsync("excel", Arg.Any<CancellationToken>()).Returns(
            new[] { new CourseRosterEntry("ana-op", "Ana Rincón", "excel", 40, new DateTimeOffset(2026, 10, 3, 3, 30, 0, TimeSpan.Zero)) });

        var consola = await Consola();

        Assert.Equal("2026-10-02", consola.Students.Single(s => s.CourseId == "excel").EnrolledAt);
    }

    /// <summary>
    /// «Devolver» sólo donde se puede (lote 8, 010-P2 B, T036): la fila lleva la matrícula devolvible si quien mira es
    /// <c>admin</c> y la matrícula salió por la puerta con algo cobrado; a un instructor, nunca; una de la ruta directa
    /// o una gratis, nunca.
    /// </summary>
    [Fact]
    public async Task Solo_el_admin_recibe_la_matricula_devolvible_y_solo_en_filas_de_la_puerta_con_cobro()
    {
        DosCursosConUnAlumnoCadaUno();
        var dia = new DateTimeOffset(2026, 6, 20, 15, 30, 0, TimeSpan.Zero);
        _metrics.GetCourseRosterAsync("excel", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CourseRosterEntry("ana-op", "Ana Rincón", "excel", 40, dia, MatriculaDeLaPuerta: "mat-ana", Total: 180_000m),
            new CourseRosterEntry("caro-op", "Caro Díaz", "excel", 0, dia, MatriculaDeLaPuerta: "mat-caro", Total: 0m),
        });
        CatalogoConTresCursos();
        ComoAdmin();

        var deAdmin = await Consola();

        Assert.Equal("mat-ana", deAdmin.Students.Single(s => s.Name == "Ana Rincón").MatriculaDevolvible);
        Assert.Null(deAdmin.Students.Single(s => s is { Name: "Caro Díaz", CourseId: "excel" }).MatriculaDevolvible);
        Assert.Null(deAdmin.Students.Single(s => s.Name == "Beto Salas").MatriculaDevolvible);

        ComoInstructor();
        var deInstructor = await Consola();

        Assert.All(deInstructor.Students, s => Assert.Null(s.MatriculaDevolvible));
    }

    /// <summary>
    /// La consola de un admin es la de TODO el catálogo (hallazgo 1 de 010, decidido por el director): el catálogo nombra
    /// a sus instructores <c>ins-*</c> y la consola los compara con el correo de la sesión, así que un admin que no dicta
    /// nada no veía filas y «Devolver» no aparecía nunca.
    /// </summary>
    [Fact]
    public async Task El_admin_ve_los_cursos_y_los_alumnos_de_todo_el_catalogo_aunque_no_dicte_ninguno()
    {
        DosCursosConUnAlumnoCadaUno();
        _catalog.GetForInstructorAsync(Instructor, Arg.Any<CancellationToken>()).Returns(
            new InstructorCoursesResult(Instructor, Array.Empty<InstructorCourse>(), 0, 0m, "COP"));
        _metrics.GetCourseRosterAsync("excel", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CourseRosterEntry("ana-op", "Ana Rincón", "excel", 40, new DateTimeOffset(2026, 6, 20, 15, 30, 0, TimeSpan.Zero),
                MatriculaDeLaPuerta: "mat-ana", Total: 180_000m),
        });
        CatalogoConTresCursos();
        ComoAdmin();

        var consola = await Consola();

        Assert.Equal(["git", "excel", "sql"], consola.Courses.Select(c => c.Id));
        Assert.Equal(["Ana Rincón", "Beto Salas", "Caro Díaz"], consola.Students.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal("mat-ana", consola.Students.Single(s => s.Name == "Ana Rincón").MatriculaDevolvible);
        Assert.Equal((3, 360_000m), (consola.TotalStudents, consola.TotalRevenue));
    }

    [Fact] // un instructor sigue viendo sólo lo que dicta: el catálogo entero es del admin
    public async Task El_instructor_sigue_viendo_solo_sus_cursos()
    {
        DosCursosConUnAlumnoCadaUno();
        CatalogoConTresCursos();

        var consola = await Consola();

        Assert.Equal(["excel", "sql"], consola.Courses.Select(c => c.Id));
        await _catalog.DidNotReceive().SearchAsync(Arg.Any<CourseQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact] // instructor sin cursos → lista vacía, no 500 ni null
    public async Task Sin_cursos_la_lista_de_alumnos_va_vacia()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Instructor);
        ComoInstructor();
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string>()).Returns("$0");
        _catalog.GetForInstructorAsync(Instructor, Arg.Any<CancellationToken>()).Returns(
            new InstructorCoursesResult(Instructor, Array.Empty<InstructorCourse>(), 0, 0m, "COP"));

        var consola = await Consola();

        Assert.Empty(consola.Students);
        await _metrics.DidNotReceive().GetCourseRosterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
