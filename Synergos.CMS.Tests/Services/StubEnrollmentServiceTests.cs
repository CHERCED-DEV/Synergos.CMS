using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

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

    /// <summary>
    /// Elegir el plan de cuotas cobra el TOTAL DEL PLAN, no el precio del curso.
    /// </summary>
    /// <remarks>
    /// <b>Era plata, y no fallaba</b> (#102): la UI manda <c>planId</c> desde siempre, el
    /// motor nunca lo recibió, y el alumno que elegía «3 cuotas» pagaba el contado — ni el
    /// cobro, ni el expediente de la matrícula, ni un log decían nada.
    ///
    /// <para><b>El fixture EXIGE la regla porque los dos montos son DISTINTOS.</b> El plan de
    /// cuotas lleva un recargo del 8 %, así que 320.000 de contado son 345.600 financiados. Con
    /// un plan que costara lo mismo que el curso —el de contado— el test pasaría en verde con
    /// el planId descartado, que es exactamente el defecto.</para>
    /// </remarks>
    [Fact]
    public async Task Enroll_ConPlanDeCuotas_CobraElTotalDelPlan()
    {
        var (svc, catalog) = Make();

        var detail = await catalog.GetCourseAsync(PaidCourse);
        var cuotas = detail!.Plans.Single(p => p.Installments > 1);
        Assert.NotEqual(detail.Course.Price, cuotas.Total);   // el fixture exige la regla

        var result = await svc.EnrollAsync(PaidCourse, StudentJuan(), cuotas.Code);

        Assert.Equal(cuotas.Total, result.Amount);
    }

    /// <summary>
    /// Sin plan elegido se cobra el precio del curso — el comportamiento de siempre.
    /// </summary>
    [Fact]
    public async Task Enroll_SinPlan_CobraElPrecioDelCurso()
    {
        var (svc, catalog) = Make();
        var detail = await catalog.GetCourseAsync(PaidCourse);

        var result = await svc.EnrollAsync(PaidCourse, StudentJuan());

        Assert.Equal(detail!.Course.Price, result.Amount);
    }

    /// <summary>
    /// Un plan que no existe se RECHAZA; no cae al precio del curso.
    /// </summary>
    /// <remarks>
    /// Caer sería el defecto original con otro nombre: el alumno elige algo y se le cobra otra
    /// cosa, en silencio. Es plata y el error se nota al instante, así que falla a la vista.
    /// </remarks>
    [Fact]
    public async Task Enroll_ConPlanInexistente_Rechaza()
    {
        var (svc, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.EnrollAsync(PaidCourse, StudentJuan(), "plan-que-no-existe"));
    }

    /// <summary>
    /// «Mi aprendizaje» lista las matrículas ACTIVAS del alumno, no las pendientes de pago.
    /// </summary>
    /// <remarks>
    /// <b>El fixture mete las dos</b> —una gratis (que queda Activa al instante) y una de pago
    /// (que queda PendingPayment)— porque con una sola el test pasaría en verde sin el filtro.
    /// Una matrícula pendiente es un carrito abandonado: listarla entre «mis cursos» le diría
    /// al alumno que tiene acceso a algo que no pagó.
    /// </remarks>
    [Fact]
    public async Task GetEnrollments_SoloLasActivas()
    {
        var (svc, _) = Make();
        var juan = StudentJuan();

        await svc.EnrollAsync(FreeCourse, juan);    // → Active
        await svc.EnrollAsync(PaidCourse, juan);    // → PendingPayment

        var mine = await svc.GetEnrollmentsAsync(juan.Email);

        Assert.Equal(FreeCourse, Assert.Single(mine).CourseId);
    }

    /// <summary>
    /// Matricularse dos veces al mismo curso deja UNA matrícula, no dos.
    /// </summary>
    /// <remarks>
    /// <para><b>Qué cierra</b> (#121). <c>POST /academy/enroll</c> no lleva llave de idempotencia,
    /// así que un enroll que sale del servidor y cuya respuesta se pierde se repite al volver a
    /// pulsar. Hasta #117 eso ni llegaba —el cliente fabricaba el acuse—; hoy llega.</para>
    ///
    /// <para><b>EL FIXTURE USA EL CURSO GRATIS A PROPÓSITO, Y ES LA MITAD QUE CUESTA.</b> La rama
    /// gratis activa la matrícula EN EL ACTO y nunca pasa por <c>ConfirmAsync</c>, así que el
    /// segundo enroll dejaba una segunda matrícula <b>Active</b>, permanente. Con un curso de
    /// pago el segundo se queda en <c>PendingPayment</c> —sólo uno se confirma— y el defecto
    /// pasaría en VERDE.</para>
    ///
    /// <para>Y se comprueba el <b>identificador</b>, no sólo la cuenta: devolver una matrícula
    /// nueva que apunte a la misma persona seguiría partiendo el expediente en dos.</para>
    /// </remarks>
    [Fact]
    public async Task Enroll_DosVeces_AlMismoCursoGratis_DejaUNA()
    {
        var (svc, _) = Make();
        var juan = StudentJuan();

        var primera = await svc.EnrollAsync(FreeCourse, juan);
        var segunda = await svc.EnrollAsync(FreeCourse, juan);

        Assert.True(primera.Enrolled);
        Assert.True(segunda.Enrolled);
        Assert.Equal(primera.EnrollmentId, segunda.EnrollmentId);

        var mine = await svc.GetEnrollmentsAsync(juan.Email);
        Assert.Equal(FreeCourse, Assert.Single(mine).CourseId);
    }

    /// <summary>
    /// Quien YA tiene el curso activo no abre una segunda sesión de cobro.
    /// </summary>
    /// <remarks>
    /// <b>Esto es plata</b> (#121): sin el corte, matricularse de nuevo a un curso de pago que ya
    /// se tiene abría una sesión nueva y dejaba pagar dos veces lo mismo. El curso se activa
    /// primero confirmando, que es el único camino por el que un curso de pago llega a
    /// <c>Active</c>.
    /// </remarks>
    [Fact]
    public async Task Enroll_CuandoYaEstaActivo_NoAbreOtraSesionDeCobro()
    {
        var pagos = new ContandoSesiones();
        var (svc, _) = Make(pagos);
        var juan = StudentJuan();

        var primera = await svc.EnrollAsync(PaidCourse, juan);
        await svc.ConfirmAsync(primera.OrderRef!);             // → Active
        Assert.Equal(1, pagos.Sesiones);

        var segunda = await svc.EnrollAsync(PaidCourse, juan);

        Assert.Equal(1, pagos.Sesiones);                       // NO se abrió otra
        Assert.Equal(primera.EnrollmentId, segunda.EnrollmentId);
    }

    /// <summary>
    /// Sólo una matrícula ACTIVA bloquea: una pendiente de pago no encierra al alumno.
    /// </summary>
    /// <remarks>
    /// <para><b>El alcance del corte, escrito en vez de omitido</b> (#121). Devolver la matrícula
    /// pendiente devolvería su sesión de cobro, que puede estar muerta, y dejaría al alumno sin
    /// forma de pagar. Un segundo pendiente es ruido —sólo uno se confirma— y no daño, así que se
    /// prefiere el ruido al encierro. El día que las sesiones se puedan revalidar, esto se
    /// revisa.</para>
    ///
    /// <para><b>Y la otra mitad NO se puede probar por la costura, que es un hallazgo en sí</b>:
    /// el corte mira <c>Active</c> y por tanto <c>Cancelled</c> tampoco bloquea —la lección del
    /// defecto #41, donde encontrar un registro encerraba a quien lo había deshecho—. Pero
    /// <b>NADA en el repo pone una matrícula en <c>Cancelled</c></b>: <c>IEnrollmentService</c> no
    /// tiene <c>CancelAsync</c> y ningún código asigna ese estado. Es un valor del vocabulario sin
    /// camino de entrada — el espejo de <c>feedback_no_read_without_a_write_path</c>. Se deja
    /// escrito acá en vez de fabricar el estado con un doble, que probaría el doble y no la
    /// regla.</para>
    /// </remarks>
    [Fact]
    public async Task Enroll_ConUnaPendienteDePago_NoQuedaEncerrado()
    {
        var pagos = new ContandoSesiones();
        var (svc, _) = Make(pagos);
        var juan = StudentJuan();

        await svc.EnrollAsync(PaidCourse, juan);   // → PendingPayment
        await svc.EnrollAsync(PaidCourse, juan);   // sigue pudiendo intentar pagar

        Assert.Equal(2, pagos.Sesiones);
        Assert.Empty(await svc.GetEnrollmentsAsync(juan.Email));   // ninguna activa todavía
    }

    /// <summary>Un proveedor que cuenta cuántas sesiones de cobro se abrieron.</summary>
    private sealed class ContandoSesiones : IPaymentProvider, IDisposable
    {
        private readonly StubPaymentProvider _real = new();

        /// <inheritdoc />
        public void Dispose() => _real.Dispose();
        public int Sesiones { get; private set; }

        public string ProviderKey => _real.ProviderKey;

        public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default)
        {
            Sesiones++;
            return _real.CreateSessionAsync(request, cancellationToken);
        }

        public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
            => _real.GetStatusAsync(sessionId, cancellationToken);

        public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
            => _real.CaptureAsync(sessionId, amount, cancellationToken);

        public Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default)
            => _real.VoidAsync(sessionId, cancellationToken);

        public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
            => _real.RefundAsync(sessionId, amount, cancellationToken);
    }

    [Fact] // empty: un alumno sin matrículas no lanza
    public async Task GetEnrollments_SinMatriculas_DevuelveVacio()
        => Assert.Empty(await Make().Svc.GetEnrollmentsAsync("nadie@synergos.co"));

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
        var lessonId = detail!.Modules[0].Lessons[0].Id;

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
        var lessonId = detail!.Modules[0].Lessons[0].Id;

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
        var lessonId = detail!.Modules[0].Lessons[0].Id;
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
