using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Cubre <c>GET /academy/verify/{certificateId}</c> — la puerta pública de la credencial
/// (ADR 0124).
/// </summary>
/// <remarks>
/// <para>La promesa estaba escrita desde la Ola 5: <c>Certificate.VerifyUrl</c> emitía
/// <c>/academy/verify/{id}</c> y el XML-doc del seam hablaba de "la cara pública de la
/// credencial (QR → esta verificación)". Ningún controller la servía: el empleador que
/// escaneaba el diploma recibía un 404 del CMS. Nada fallaba — simplemente la
/// documentación y el código no decían lo mismo.</para>
///
/// <para>Lo que estos tests fijan como CORRECTO y no se debe "mejorar" después:
/// <b>la respuesta a un id desconocido y a uno falsificado es idéntica byte a byte</b>.
/// Añadir un <c>reason</c> "para ayudar a depurar" convertiría el endpoint en un oráculo:
/// "este id no existe" vs "este id existe pero no vale" ya es información sobre terceros.
/// Y el nombre del titular se publica solo si es un nombre — nunca su correo.</para>
/// </remarks>
public sealed class CertificateVerificationEndpointTests
{
    private readonly ICourseCatalogProvider _catalog = Substitute.For<ICourseCatalogProvider>();
    private readonly IEnrollmentService _enrollments = Substitute.For<IEnrollmentService>();
    private readonly ICertificateService _certificates = Substitute.For<ICertificateService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private AcademyController BuildSut() => new(_catalog, _enrollments, _certificates, _priceFormatter, _gate);

    private const string ValidId = "cert-0123456789abcdef0123456789abcdef";

    private void CredencialValida(string studentName = "Juan Pérez")
    {
        _certificates.VerifyAsync(ValidId, Arg.Any<CancellationToken>())
            .Returns(new Certificate(
                Id: ValidId,
                CourseId: "dev-git-fundamentals",
                StudentName: studentName,
                IssuedAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                VerifyUrl: $"/academy/verify/{ValidId}"));

        _catalog.GetCourseAsync("dev-git-fundamentals", Arg.Any<CancellationToken>())
            .Returns(new CourseDetail(
                Course: new CourseSummary(
                    Id: "dev-git-fundamentals",
                    Title: "Git desde cero",
                    Summary: "s",
                    Category: "Desarrollo",
                    Level: "Principiante",
                    InstructorName: "Elena",
                    CoverImageUrl: null,
                    Price: 0m,
                    Currency: "COP",
                    IsFree: true,
                    Rating: 4.5,
                    LessonCount: 3,
                    DurationMinutes: 60),
                Description: "d",
                Outcomes: Array.Empty<string>(),
                Modules: Array.Empty<CourseModule>(),
                Instructor: new CourseInstructor("ins-elena", "Elena", "h", "b", null),
                Plans: Array.Empty<CoursePricingPlan>()));
    }

    private static string BodyOf(IActionResult result)
        => JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static int StatusOf(IActionResult result)
        => Assert.IsAssignableFrom<ObjectResult>(result).StatusCode ?? 0;

    // ── Es público, que es el punto ──────────────────────────────────────

    [Fact] // el empleador que escanea el diploma no tiene cuenta aquí
    public async Task Verificar_es_ANONIMO_y_no_toca_el_gate()
    {
        _gate.IsAuthenticated.Returns(false);
        CredencialValida();

        var result = await BuildSut().VerifyCertificate(ValidId, default);

        Assert.IsNotType<UnauthorizedObjectResult>(result);
        Assert.Equal(200, StatusOf(result));
        _ = _gate.DidNotReceive().IsAuthenticated;
    }

    // ── No enumera ───────────────────────────────────────────────────────

    [Fact] // EL QUE MUERDE: desconocido y falsificado responden EXACTAMENTE igual
    public async Task Un_id_desconocido_y_uno_falsificado_responden_igual()
    {
        // El seam devuelve null para las dos cosas y no dice cuál fue — el controller
        // tampoco puede inventarse la diferencia.
        _certificates.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Certificate?)null);

        var desconocido = await BuildSut().VerifyCertificate("cert-ffffffffffffffffffffffffffffffff", default);
        var falsificado = await BuildSut().VerifyCertificate("no-es-un-id", default);
        var vacio = await BuildSut().VerifyCertificate(null, default);

        Assert.Equal(404, StatusOf(desconocido));
        Assert.Equal(StatusOf(desconocido), StatusOf(falsificado));
        Assert.Equal(StatusOf(desconocido), StatusOf(vacio));
        Assert.Equal(BodyOf(desconocido), BodyOf(falsificado));
        Assert.Equal(BodyOf(desconocido), BodyOf(vacio));
    }

    [Fact] // un id que no vale NO se convierte en una pregunta al catálogo
    public async Task Un_id_que_no_vale_no_consulta_el_catalogo()
    {
        _certificates.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Certificate?)null);

        await BuildSut().VerifyCertificate("cert-ffffffffffffffffffffffffffffffff", default);

        await _catalog.DidNotReceive().GetCourseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Qué ve el verificador ────────────────────────────────────────────

    [Fact] // happy: qué curso, quién y cuándo — que es lo que "verificar" significa
    public async Task La_credencial_valida_muestra_titular_curso_y_fecha()
    {
        CredencialValida();

        var result = await BuildSut().VerifyCertificate(ValidId, default);

        var response = Assert.IsType<AcademyController.VerifyCertificateResponse>(
            Assert.IsAssignableFrom<ObjectResult>(result).Value);
        Assert.True(response.Valid);
        Assert.Equal("Juan Pérez", response.Certificate!.StudentName);
        Assert.Equal("Git desde cero", response.Certificate.CourseTitle);
        Assert.Equal("dev-git-fundamentals", response.Certificate.CourseId);
        Assert.Equal(ValidId, response.Certificate.Id);
    }

    [Fact] // EL QUE MUERDE: nunca sale el correo del titular en un endpoint anónimo
    public async Task La_verificacion_publica_NUNCA_publica_el_correo_del_titular()
    {
        // El motor cae al identificador cuando no hay nombre de matrícula
        // (`ResolveStudentName` devuelve el propio `student`). Eso NO se publica.
        CredencialValida(studentName: "juan@synergos.co");

        var result = await BuildSut().VerifyCertificate(ValidId, default);

        var response = Assert.IsType<AcademyController.VerifyCertificateResponse>(
            Assert.IsAssignableFrom<ObjectResult>(result).Value);
        Assert.True(response.Valid);           // la credencial SIGUE siendo válida…
        Assert.Null(response.Certificate!.StudentName); // …pero sin nombre publicable
        Assert.DoesNotContain("@", BodyOf(result), StringComparison.Ordinal);
    }

    [Fact] // el curso borrado del catálogo no tumba la verificación: la credencial existió
    public async Task Si_el_curso_ya_no_esta_en_el_catalogo_la_credencial_sigue_valiendo()
    {
        CredencialValida();
        _catalog.GetCourseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CourseDetail?)null);

        var result = await BuildSut().VerifyCertificate(ValidId, default);

        var response = Assert.IsType<AcademyController.VerifyCertificateResponse>(
            Assert.IsAssignableFrom<ObjectResult>(result).Value);
        Assert.True(response.Valid);
        Assert.Null(response.Certificate!.CourseTitle);
    }
}
