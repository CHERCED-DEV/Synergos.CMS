using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests de AUTORIZACIÓN de <see cref="AcademyController"/> (T2-Educación). Los dos
/// controllers que ADR 0108 NO llegó a listar: su inventario de "trabajo abierto" se hizo
/// mirando los verticales del piloto, y estos quedaron fuera.
/// </summary>
/// <remarks>
/// El hueco grave aquí no era de lectura sino de ESCRITURA: <c>POST /progress</c> tomaba al
/// alumno de <c>request.Student</c>, así que cualquiera marcaba lecciones como completadas
/// en el expediente de otro. Un registro académico que cualquiera puede escribir no
/// registra nada — y como el certificado se emite al llegar a 100%, eso encadenaba a
/// acreditar a alguien que no estudió.
/// </remarks>
public sealed class AcademyControllerAuthTests
{
    private const string Yo = "alumna@correo.co";
    private const string Otro = "victima@correo.co";

    private readonly ICourseCatalogProvider _catalog = Substitute.For<ICourseCatalogProvider>();
    private readonly IEnrollmentService _enrollments = Substitute.For<IEnrollmentService>();
    private readonly ICertificateService _certificates = Substitute.For<ICertificateService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private AcademyController BuildSut() => new(_catalog, _enrollments, _certificates, _priceFormatter, _gate);

    /// <summary>Seams con datos reales: null → NRE en el mapeo, antes de afirmar nada.</summary>
    private void SeamsConDatos()
    {
        _enrollments.MarkLessonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CourseProgress("curso-1", Array.Empty<string>(), 10));
        _enrollments.GetProgressAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CourseProgress("curso-1", Array.Empty<string>(), 10));
        _catalog.SearchAsync(Arg.Any<CourseQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CourseSearchResult(Array.Empty<CourseSummary>(), 0));
        _catalog.GetForInstructorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InstructorCoursesResult(Yo, Array.Empty<InstructorCourse>(), 0, 0m, "COP"));
    }

    private void Anonimo()
    {
        SeamsConDatos();
        _gate.IsAuthenticated.Returns(false);
    }

    private void Alumna()
    {
        SeamsConDatos();
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Yo);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

    private void Instructor()
    {
        SeamsConDatos();
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Yo);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private static void AssertForbidden(IActionResult result)
        => Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);

    // ══════════════ El expediente del alumno ══════════════

    [Fact] // empty
    public async Task GetProgress_Anonimo_Da401_YNoTocaElSeam()
    {
        Anonimo();

        Assert.IsType<UnauthorizedObjectResult>(await BuildSut().GetProgress("curso-1", null, default));
        await _enrollments.DidNotReceive().GetProgressAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // EL QUE MUERDE: el IDOR de ESCRITURA — no se marca en el expediente ajeno
    public async Task MarkProgress_ConAlumnoAjenoEnElBody_MarcaEnElMio()
    {
        Alumna();

        await BuildSut().MarkProgress(
            new AcademyController.MarkProgressRequest(Student: Otro, Course: "curso-1", Lesson: "lec-1"),
            default);

        await _enrollments.Received(1).MarkLessonAsync("curso-1", "lec-1", Yo, Arg.Any<CancellationToken>());
        await _enrollments.DidNotReceive().MarkLessonAsync(Arg.Any<string>(), Arg.Any<string>(), Otro, Arg.Any<CancellationToken>());
    }

    [Fact] // anónimo no escribe en NINGÚN expediente
    public async Task MarkProgress_Anonimo_Da401_YNoTocaElSeam()
    {
        Anonimo();

        var result = await BuildSut().MarkProgress(
            new AcademyController.MarkProgressRequest(Student: Otro, Course: "curso-1", Lesson: "lec-1"),
            default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _enrollments.DidNotReceive().MarkLessonAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // el certificado que se consulta es el PROPIO — con ?student= era enumerable
    public async Task GetCertificate_Logueada_PideElDelGate()
    {
        Alumna();

        await BuildSut().GetCertificate("curso-1", default);

        await _certificates.Received(1).GetAsync(Yo, "curso-1", Arg.Any<CancellationToken>());
    }

    // ══════════════ El panel del INSTRUCTOR ══════════════

    [Fact] // filter: estar logueada NO es ser instructora
    public async Task InstructorCourses_AlumnaSinRol_Da403_YNoTocaElSeam()
    {
        Alumna();

        AssertForbidden(await BuildSut().InstructorCourses(default));
        await _catalog.DidNotReceive().GetForInstructorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // happy: y ve LOS SUYOS, no los del colega
    public async Task InstructorCourses_Instructor_PideLosDelGate()
    {
        Instructor();

        await BuildSut().InstructorCourses(default);

        await _catalog.Received(1).GetForInstructorAsync(Yo, Arg.Any<CancellationToken>());
    }

    [Fact] // publicar un curso al catálogo del sitio pide rol
    public async Task PublishCourse_AlumnaSinRol_Da403()
    {
        Alumna();

        AssertForbidden(await BuildSut().PublishCourse(null, default));
    }

    // ══════════════ Lo PÚBLICO sigue público (regresión) ══════════════

    [Fact] // el catálogo de cursos se ve sin cuenta: es la vitrina
    public async Task Courses_Anonimo_NoPideSesion()
    {
        Anonimo();

        Assert.IsNotType<UnauthorizedObjectResult>(await BuildSut().Courses(null, null, null, default));
    }
}

/// <summary>
/// Tests de AUTORIZACIÓN de <see cref="TravelController"/> (T2-Viajes).
/// </summary>
/// <remarks>
/// "Mis viajes" salía de <c>?traveler=&lt;email&gt;</c>. Un itinerario con fechas no es solo
/// un dato de compra: dice cuándo la persona no está en su casa.
/// <para>Lo que estos tests fijan COMO CORRECTO y no se debe "arreglar" después:
/// <c>order/{orderRef}</c> y su cancelación siguen sin pedir sesión. Ese ref es
/// <c>trip_{Guid:N}</c> —128 bits— y tenerlo ES la autorización. Es lo que permite que
/// quien compró como invitado vuelva a su reserva. Pedir sesión ahí rompe la compra de
/// invitado sin cerrar nada.</para>
/// </remarks>
public sealed class TravelControllerAuthTests
{
    private const string Yo = "viajera@correo.co";

    private readonly IRoomAvailabilityProvider _hotels = Substitute.For<IRoomAvailabilityProvider>();
    private readonly IFlightAvailabilityProvider _flights = Substitute.For<IFlightAvailabilityProvider>();
    private readonly ICarRentalProvider _cars = Substitute.For<ICarRentalProvider>();
    private readonly ITravelCartService _cart = Substitute.For<ITravelCartService>();
    private readonly IStayContentProvider _stays = Substitute.For<IStayContentProvider>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private TravelController BuildSut() => new(
        _hotels, _flights, _cars, _cart, _stays, _priceFormatter, _gate);

    [Fact] // empty: el itinerario ajeno ya no se pide por correo — ni se toca el seam
    public async Task Trips_Anonimo_Da401_YNoTocaElSeam()
    {
        _gate.IsAuthenticated.Returns(false);

        Assert.IsType<UnauthorizedObjectResult>(await BuildSut().Trips(default));
        await _cart.DidNotReceive().GetTripsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // happy: se piden los del gate
    public async Task Trips_Logueada_PideLosDelGate()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Yo);

        await BuildSut().Trips(default);

        await _cart.Received(1).GetTripsAsync(Yo, Arg.Any<CancellationToken>());
    }

    [Fact] // regresión DELIBERADA: la reserva del invitado sigue accesible por su ref
    public async Task Order_Anonimo_NoPideSesion_ElRefEsLaCredencial()
    {
        _gate.IsAuthenticated.Returns(false);

        var result = await BuildSut().Order("trip_deadbeefdeadbeefdeadbeefdeadbeef", default);

        Assert.IsNotType<UnauthorizedObjectResult>(result);
    }
}
