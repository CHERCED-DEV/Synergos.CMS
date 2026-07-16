using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubEnrollmentService"/> (seam <see cref="IEnrollmentService"/>,
/// motor de matrícula + progreso del LMS — dominio Educación): los 4 casos
/// canónicos (ADR 0075) — empty / happy / filter / idempotent — más la rama
/// gratis, el progreso y el certificado al 100%. Compone los seams reales
/// (<see cref="StubCourseCatalogProvider"/> + <see cref="StubPaymentProvider"/>)
/// para ejercer el flujo end-to-end enroll → pagar → confirmar → progreso → cert.
/// </summary>
public class StubEnrollmentServiceTests
{
    private const string PaidCourse = "dev-clean-architecture"; // 320.000 COP
    private const string FreeCourse = "dev-git-fundamentals";   // 0 COP

    private static StubContentStream ContentStream()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock);
    }

    private static ICourseCatalogProvider Catalog() => new StubCourseCatalogProvider(ContentStream());

    private static (IEnrollmentService Svc, ICourseCatalogProvider Catalog) Make(IPaymentProvider? payments = null)
    {
        var catalog = Catalog();
        var svc = new StubEnrollmentService(catalog, payments ?? new StubPaymentProvider());
        return (svc, catalog);
    }

    private static Student StudentJuan() => new("Juan Pérez", "juan@synergos.co");

    [Fact] // empty: inscribir en un curso inexistente lanza
    public async Task Enroll_UnknownCourse_Throws()
    {
        var (svc, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.EnrollAsync("no-existe", StudentJuan()));
    }

    [Fact] // inválido: alumno sin email lanza
    public async Task Enroll_InvalidStudent_Throws()
    {
        var (svc, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.EnrollAsync(PaidCourse, new Student("Juan", "")));
    }

    [Fact] // happy (pago): enroll abre sesión de pago por el precio del catálogo
    public async Task Enroll_PaidCourse_OpensPaymentSession()
    {
        var (svc, _) = Make();

        var result = await svc.EnrollAsync(PaidCourse, StudentJuan());

        Assert.False(result.Enrolled);
        Assert.False(string.IsNullOrWhiteSpace(result.OrderRef));
        Assert.False(string.IsNullOrWhiteSpace(result.PaymentSessionId));
        Assert.Equal(320_000m, result.Amount); // precio del catálogo, no del cliente
        Assert.Equal("COP", result.Currency);
    }

    [Fact] // happy (gratis): enroll de curso gratis activa la matrícula sin pago
    public async Task Enroll_FreeCourse_EnrollsImmediately()
    {
        var (svc, _) = Make();

        var result = await svc.EnrollAsync(FreeCourse, StudentJuan());

        Assert.True(result.Enrolled);
        Assert.False(string.IsNullOrWhiteSpace(result.EnrollmentId));
        Assert.Null(result.OrderRef);
        Assert.Null(result.PaymentSessionId);
    }

    [Fact] // happy: enroll → confirm captura y deja la matrícula Active
    public async Task EnrollThenConfirm_ActivatesEnrollment()
    {
        var (svc, _) = Make();
        var enroll = await svc.EnrollAsync(PaidCourse, StudentJuan());

        var confirm = await svc.ConfirmAsync(enroll.OrderRef!);

        Assert.Equal(EnrollmentStatus.Active.ToString(), confirm.Status);
        Assert.False(string.IsNullOrWhiteSpace(confirm.EnrollmentId));
        Assert.Equal(PaidCourse, confirm.CourseId);
    }

    [Fact] // inválido: confirmar un orderRef inexistente lanza
    public async Task Confirm_UnknownOrder_Throws()
    {
        var (svc, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmAsync("enr_doesnotexist"));
    }

    [Fact] // idempotent: re-confirmar el mismo orderRef da la misma matrícula
    public async Task Confirm_Twice_IsIdempotent()
    {
        var (svc, _) = Make();
        var enroll = await svc.EnrollAsync(PaidCourse, StudentJuan());

        var first = await svc.ConfirmAsync(enroll.OrderRef!);
        var second = await svc.ConfirmAsync(enroll.OrderRef!);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.EnrollmentId, second.EnrollmentId);
    }

    // ── Progreso ───────────────────────────────────────────────────────

    [Fact] // empty: alumno sin progreso previo → 0% + lista vacía (no lanza)
    public async Task GetProgress_NoProgress_ReturnsZero()
    {
        var (svc, _) = Make();

        var progress = await svc.GetProgressAsync(PaidCourse, "nadie@synergos.co");

        Assert.Empty(progress.CompletedLessonIds);
        Assert.Equal(0, progress.Percent);
    }

    [Fact] // happy: marcar una lección sube el % proporcional al total del curso
    public async Task MarkLesson_AdvancesPercent()
    {
        var (svc, catalog) = Make();
        var detail = await catalog.GetCourseAsync(PaidCourse);
        var lessons = detail!.Modules.SelectMany(m => m.Lessons).ToList();

        var progress = await svc.MarkLessonAsync(PaidCourse, lessons[0].Id, "juan@synergos.co");

        Assert.Contains(lessons[0].Id, progress.CompletedLessonIds);
        var expected = (int)Math.Round(1 * 100.0 / lessons.Count, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, progress.Percent);
        Assert.Equal(lessons[0].Id, progress.LastLessonId);
    }

    [Fact] // inválido: marcar una lección que no existe en el curso lanza
    public async Task MarkLesson_UnknownLesson_Throws()
    {
        var (svc, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.MarkLessonAsync(PaidCourse, "les-no-existe", "juan@synergos.co"));
    }

    [Fact] // idempotent: marcar la misma lección dos veces no infla el % ni duplica
    public async Task MarkLesson_Twice_IsIdempotent()
    {
        var (svc, catalog) = Make();
        var detail = await catalog.GetCourseAsync(PaidCourse);
        var lessonId = detail!.Modules.First().Lessons.First().Id;

        var first = await svc.MarkLessonAsync(PaidCourse, lessonId, "juan@synergos.co");
        var second = await svc.MarkLessonAsync(PaidCourse, lessonId, "juan@synergos.co");

        Assert.Equal(first.Percent, second.Percent);
        Assert.Single(second.CompletedLessonIds);
    }

    [Fact] // filter: el progreso es por (alumno,curso) — otro alumno no lo ve
    public async Task Progress_IsScopedPerStudentAndCourse()
    {
        var (svc, catalog) = Make();
        var detail = await catalog.GetCourseAsync(PaidCourse);
        var lessonId = detail!.Modules.First().Lessons.First().Id;

        await svc.MarkLessonAsync(PaidCourse, lessonId, "juan@synergos.co");

        var otherStudent = await svc.GetProgressAsync(PaidCourse, "ana@synergos.co");
        Assert.Equal(0, otherStudent.Percent);

        var otherCourse = await svc.GetProgressAsync(FreeCourse, "juan@synergos.co");
        Assert.Equal(0, otherCourse.Percent);
    }

    // ── Certificado ────────────────────────────────────────────────────

    [Fact] // certificado: null mientras el progreso < 100%
    public async Task GetCertificate_BeforeCompletion_ReturnsNull()
    {
        var (svc, catalog) = Make();
        var detail = await catalog.GetCourseAsync(PaidCourse);
        var lessonId = detail!.Modules.First().Lessons.First().Id;
        await svc.MarkLessonAsync(PaidCourse, lessonId, "juan@synergos.co");

        Assert.Null(await svc.GetCertificateAsync(PaidCourse, "juan@synergos.co"));
    }

    [Fact] // certificado: al 100% se emite; re-emitir es idempotente (mismo id)
    public async Task GetCertificate_At100Percent_IsIssued_AndIdempotent()
    {
        var (svc, catalog) = Make();
        await svc.EnrollAsync(PaidCourse, StudentJuan()); // matrícula → nombre del alumno
        var detail = await catalog.GetCourseAsync(PaidCourse);
        var lessons = detail!.Modules.SelectMany(m => m.Lessons).ToList();

        CourseProgress last = null!;
        foreach (var lesson in lessons)
        {
            last = await svc.MarkLessonAsync(PaidCourse, lesson.Id, "juan@synergos.co");
        }
        Assert.Equal(100, last.Percent);

        var cert = await svc.GetCertificateAsync(PaidCourse, "juan@synergos.co");
        Assert.NotNull(cert);
        Assert.Equal(PaidCourse, cert!.CourseId);
        Assert.False(string.IsNullOrWhiteSpace(cert.VerifyUrl));

        // Idempotente: re-emitir devuelve el mismo id.
        var again = await svc.GetCertificateAsync(PaidCourse, "juan@synergos.co");
        Assert.Equal(cert.Id, again!.Id);
    }

    // ── Durabilidad (doc 25 · ADR 0105) ────────────────────────────────

    [Fact] // durable: matrícula + progreso sobreviven el reemplazo del servicio
    public async Task State_SurvivesServiceReplacement_ViaSharedStore()
    {
        // Los estados que el motor toca deben ser durables: la matrícula
        // ("enrollments"), el progreso ("course-progress") y la sesión de pago que
        // ConfirmAsync captura. Con el store GENÉRICO basta UNA instancia para las
        // tres familias — el resourceType las aísla entre sí.
        var store = new InMemoryJsonEntityStore();
        var catalog = Catalog();
        var beforeRestart = new StubEnrollmentService(catalog, new StubPaymentProvider(store), null, store, null);

        var enroll = await beforeRestart.EnrollAsync(PaidCourse, StudentJuan());
        var lessons = (await catalog.GetCourseAsync(PaidCourse))!.Modules.SelectMany(m => m.Lessons).ToList();
        await beforeRestart.MarkLessonAsync(PaidCourse, lessons[0].Id, "juan@synergos.co");

        // "Reinicio": instancia NUEVA del servicio + del PSP sobre el MISMO store.
        var afterRestart = new StubEnrollmentService(catalog, new StubPaymentProvider(store), null, store, null);

        // 1) La matrícula pendiente se resuelve por orderRef y confirma tras el reinicio.
        var confirm = await afterRestart.ConfirmAsync(enroll.OrderRef!);
        Assert.Equal(EnrollmentStatus.Active.ToString(), confirm.Status);
        Assert.Equal(enroll.EnrollmentId, confirm.EnrollmentId);

        // 2) El progreso (HashSet rehidratado desde la lista persistida) sobrevive.
        var progress = await afterRestart.GetProgressAsync(PaidCourse, "juan@synergos.co");
        Assert.Contains(lessons[0].Id, progress.CompletedLessonIds);
        Assert.Equal(lessons[0].Id, progress.LastLessonId);

        // 3) Marcar la MISMA lección tras el reinicio sigue siendo idempotente.
        var remark = await afterRestart.MarkLessonAsync(PaidCourse, lessons[0].Id, "juan@synergos.co");
        Assert.Single(remark.CompletedLessonIds);

        // 4) Las métricas del panel cuentan la matrícula persistida UNA sola vez.
        var stats = await afterRestart.GetCourseStatsAsync(PaidCourse);
        Assert.Equal(1, stats.Students);
        Assert.Equal(320_000m, stats.Revenue);
    }
}
