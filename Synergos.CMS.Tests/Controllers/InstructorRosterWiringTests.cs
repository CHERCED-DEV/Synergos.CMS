using Microsoft.AspNetCore.Mvc;
using NSubstitute;
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

    private AcademyController BuildSut()
        => new(_catalog, _enrollments, _certificates, _priceFormatter, _gate, _metrics);

    private static CourseSummary Course(string id, string title) => new(
        Id: id, Title: title, Summary: "Resumen", Category: "Datos", Level: "beginner",
        InstructorName: "Elena Restrepo", CoverImageUrl: null, Price: 180_000m, Currency: "COP",
        IsFree: false, Rating: 4.8, LessonCount: 10, DurationMinutes: 300);

    private void DosCursosConUnAlumnoCadaUno()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Instructor);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
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

    [Fact] // instructor sin cursos → lista vacía, no 500 ni null
    public async Task Sin_cursos_la_lista_de_alumnos_va_vacia()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Instructor);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string>()).Returns("$0");
        _catalog.GetForInstructorAsync(Instructor, Arg.Any<CancellationToken>()).Returns(
            new InstructorCoursesResult(Instructor, Array.Empty<InstructorCourse>(), 0, 0m, "COP"));

        var consola = await Consola();

        Assert.Empty(consola.Students);
        await _metrics.DidNotReceive().GetCourseRosterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
