using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Smoke de la OLA 5 Educación (doc 21 §2.4) — las extensiones ADITIVAS del motor
/// LMS: certificado verificable dedicado (<see cref="ICertificateService"/>), panel
/// del instructor (<see cref="ICourseCatalogProvider.GetForInstructorAsync"/> +
/// <see cref="ICourseCatalogProvider.PublishCourseAsync"/>), métricas de matrícula
/// (<see cref="IEnrollmentMetrics"/>) y tracking del ciclo de aprendizaje
/// (enrolled→in-progress→completed). Compone los seams reales end-to-end.
/// </summary>
public class AcademyOla5SmokeTests
{
    private const string PaidCourse = "dev-clean-architecture"; // 320.000 COP, ins-elena
    private const string FreeCourse = "dev-git-fundamentals";   // 0 COP, ins-elena
    private const string Instructor = "ins-elena";

    private static StubContentStream ContentStream()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock);
    }

    // Arma catálogo + matrícula (con tracking academy + métricas enchufadas) + certificado,
    // igual que el composer, para ejercer el flujo real.
    private static (StubCourseCatalogProvider Catalog, StubEnrollmentService Enroll, StubCertificateService Cert, StubOrderTrackingService Tracking) Make()
    {
        var catalog = new StubCourseCatalogProvider(ContentStream());
        var tracking = new StubOrderTrackingService(StubEnrollmentService.AcademyPipeline, null);
        var enroll = new StubEnrollmentService(catalog, new StubPaymentProvider(), tracking, null);
        catalog.EnrollmentMetrics = enroll; // property injection, como el composer
        // El firmante del id es OBLIGATORIO desde ADR 0124 — no hay forma de construir el
        // seam sin él, que es el punto: un certificado con id derivable no es una credencial.
        var cert = new StubCertificateService(
            catalog, enroll, new HmacCertificateIdSigner(Encoding.UTF8.GetBytes("llave-de-prueba")));
        return (catalog, enroll, cert, tracking);
    }

    private static Student StudentJuan() => new("Juan Pérez", "juan@synergos.co");

    private static async Task CompleteCourseAsync(StubEnrollmentService enroll, ICourseCatalogProvider catalog, string courseId, string student)
    {
        var detail = await catalog.GetCourseAsync(courseId);
        foreach (var lesson in detail!.Modules.SelectMany(m => m.Lessons))
        {
            await enroll.MarkLessonAsync(courseId, lesson.Id, student);
        }
    }

    // ── Certificado (seam dedicado) ─────────────────────────────────────

    [Fact] // empty: sin completar → null (no lanza)
    public async Task Certificate_BeforeCompletion_ReturnsNull()
    {
        var (catalog, enroll, cert, _) = Make();
        await enroll.EnrollAsync(FreeCourse, StudentJuan());

        Assert.Null(await cert.GetAsync("juan@synergos.co", FreeCourse));
    }

    [Fact] // happy + idempotent: al 100% emite; re-emitir da el mismo id; verify público OK
    public async Task Certificate_At100_IssuesAndVerifies_Idempotent()
    {
        var (catalog, enroll, cert, _) = Make();
        await enroll.EnrollAsync(FreeCourse, StudentJuan());
        await CompleteCourseAsync(enroll, catalog, FreeCourse, "juan@synergos.co");

        var issued = await cert.GetAsync("juan@synergos.co", FreeCourse);
        Assert.NotNull(issued);
        Assert.Equal(FreeCourse, issued!.CourseId);
        Assert.False(string.IsNullOrWhiteSpace(issued.VerifyUrl));

        var again = await cert.GetAsync("juan@synergos.co", FreeCourse);
        Assert.Equal(issued.Id, again!.Id); // idempotente

        var verified = await cert.VerifyAsync(issued.Id); // verificación pública por id
        Assert.NotNull(verified);
        Assert.Equal(issued.Id, verified!.Id);
    }

    [Fact] // filter: verificar un id inexistente → null
    public async Task Certificate_VerifyUnknownId_ReturnsNull()
    {
        var (_, _, cert, _) = Make();
        Assert.Null(await cert.VerifyAsync("cert-noexiste"));
    }

    // ── Panel del instructor: métricas + totales ────────────────────────

    [Fact] // empty: instructor sin cursos → lista vacía + totales en cero
    public async Task Instructor_Unknown_ReturnsEmpty()
    {
        var (catalog, _, _, _) = Make();
        var result = await catalog.GetForInstructorAsync("ins-fantasma");

        Assert.Empty(result.Courses);
        Assert.Equal(0, result.TotalStudents);
        Assert.Equal(0m, result.TotalRevenue);
    }

    [Fact] // happy: lista los cursos del instructor con métricas de matrícula reales
    public async Task Instructor_ListsCoursesWithMetrics()
    {
        var (catalog, enroll, _, _) = Make();
        // 1 alumno gratis + 1 alumno de pago confirmado en cursos de ins-elena.
        await enroll.EnrollAsync(FreeCourse, new Student("Ana", "ana@synergos.co"));
        var paid = await enroll.EnrollAsync(PaidCourse, StudentJuan());
        await enroll.ConfirmAsync(paid.OrderRef!);

        var result = await catalog.GetForInstructorAsync(Instructor);

        Assert.NotEmpty(result.Courses);
        var paidRow = result.Courses.Single(c => c.Course.Id == PaidCourse);
        Assert.Equal(1, paidRow.Metrics.Students);
        Assert.Equal(320_000m, paidRow.Metrics.Revenue); // ingreso del curso de pago
        Assert.True(result.TotalStudents >= 2);
        Assert.Equal(320_000m, result.TotalRevenue);
    }

    // ── Publicar curso (curriculum builder) ─────────────────────────────

    [Fact] // inválido: sin módulos lanza
    public async Task Publish_NoModules_Throws()
    {
        var (catalog, _, _, _) = Make();
        var draft = new CourseDraft(
            Title: "Curso vacío", Summary: "s", Description: "d", School: "Escuela",
            Category: "Desarrollo", Level: "Principiante", InstructorId: "ins-nuevo",
            Price: 100_000m, Modules: Array.Empty<CourseDraftModule>());

        await Assert.ThrowsAsync<ArgumentException>(() => catalog.PublishCourseAsync(draft));
    }

    [Fact] // happy: publicar aparece en catálogo + detalle; lecciones referencian el feed
    public async Task Publish_AppearsInCatalogAndDetail()
    {
        var (catalog, _, _, _) = Make();
        var draft = new CourseDraft(
            Title: "Rust para escépticos", Summary: "Rust sin dolor", Description: "Curso nuevo",
            School: "Escuela de Sistemas", Category: "Desarrollo", Level: "Avanzado",
            InstructorId: "ins-nuevo", Price: 260_000m,
            Modules: new[]
            {
                new CourseDraftModule("Fundamentos", new[]
                {
                    new CourseDraftLesson("Ownership", "https://v/1", 12),
                    new CourseDraftLesson("Borrowing", "https://v/2", 15),
                }),
            });

        var published = await catalog.PublishCourseAsync(draft);
        Assert.False(string.IsNullOrWhiteSpace(published.Course.Id));
        Assert.Equal(2, published.Modules.Sum(m => m.Lessons.Count));
        // Cada lección referencia un item del IContentStream (Kind=lesson).
        Assert.All(published.Modules.SelectMany(m => m.Lessons),
            l => Assert.False(string.IsNullOrWhiteSpace(l.ContentItemId)));

        // Aparece en el search y en GetForInstructorAsync del autor.
        var search = await catalog.SearchAsync(new CourseQuery(Text: "Rust"));
        Assert.Contains(search.Courses, c => c.Id == published.Course.Id);
        var instructor = await catalog.GetForInstructorAsync("ins-nuevo");
        Assert.Contains(instructor.Courses, c => c.Course.Id == published.Course.Id);
    }

    // ── Tracking del ciclo de aprendizaje ───────────────────────────────

    [Fact] // happy: matrícula → enrolled; primera lección → in-progress; 100% → completed
    public async Task Tracking_AdvancesThroughLearningPipeline()
    {
        var (catalog, enroll, _, tracking) = Make();
        var free = await enroll.EnrollAsync(FreeCourse, StudentJuan());
        var enrollmentId = free.EnrollmentId!;

        var afterEnroll = await tracking.GetTimelineAsync(enrollmentId);
        Assert.NotNull(afterEnroll);
        Assert.Equal(StubEnrollmentService.StageEnrolled, afterEnroll!.CurrentStage);

        var detail = await catalog.GetCourseAsync(FreeCourse);
        var lessons = detail!.Modules.SelectMany(m => m.Lessons).ToList();
        await enroll.MarkLessonAsync(FreeCourse, lessons[0].Id, "juan@synergos.co");
        Assert.Equal("in-progress", (await tracking.GetTimelineAsync(enrollmentId))!.CurrentStage);

        foreach (var l in lessons.Skip(1))
        {
            await enroll.MarkLessonAsync(FreeCourse, l.Id, "juan@synergos.co");
        }
        Assert.Equal("completed", (await tracking.GetTimelineAsync(enrollmentId))!.CurrentStage);
    }

    // ── Métricas (seam de lectura) ──────────────────────────────────────

    [Fact] // empty: curso sin matrículas → ceros (no lanza)
    public async Task Metrics_NoEnrollments_ReturnsZero()
    {
        var (_, enroll, _, _) = Make();
        var stats = await ((IEnrollmentMetrics)enroll).GetCourseStatsAsync(PaidCourse);
        Assert.Equal(0, stats.Students);
        Assert.Equal(0m, stats.Revenue);
    }
}
