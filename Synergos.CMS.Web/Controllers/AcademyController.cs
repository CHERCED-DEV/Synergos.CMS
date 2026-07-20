using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del LMS (dominio Educación — OLA 4). Es el equivalente educativo del
/// <see cref="ShopCatalogController"/>/<see cref="BookingController"/>: delega el
/// catálogo + detalle del curso (currículum + instructor + planes) a
/// <see cref="ICourseCatalogProvider"/> y el flujo transaccional enroll → pagar →
/// confirmar + el progreso/certificado a <see cref="IEnrollmentService"/>,
/// formateando precios es-CO con <see cref="IPriceFormatter"/>. Expone el contrato
/// que los módulos Angular <c>course-catalog</c> + <c>course-player</c> consumen:
/// <c>GET courses · GET course/{id} · POST enroll · POST confirm · GET progress ·
/// POST progress</c>.
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en
/// los seams (Application, sin Umbraco — ADR 0002). Los seams se cambian por
/// adapters reales (Examine sobre coursePage para el catálogo; Stripe/Wompi/PayU
/// para el pago; DB de matrículas) sin tocar este controller.
///
/// <para><b>Polimorfismo Blogs.</b> El contenido editorial de cada lección NO se
/// duplica: la <see cref="CourseLesson.ContentItemId"/> referencia un item del
/// <see cref="IContentStream"/> con <c>Kind=lesson</c> (sembrado por el catálogo).
/// El módulo course-player resuelve ese cuerpo/transcripción consumiendo el feed
/// — el MISMO motor que usa Blogs — vía la abstracción, sin instanciar Blogs.</para>
/// </remarks>
[ApiController]
[Route("api/academy")]
public sealed class AcademyController : ControllerBase
{
    private readonly ICourseCatalogProvider _catalog;
    private readonly IEnrollmentService _enrollments;
    private readonly ICertificateService _certificates;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IMemberAccessGate _gate;

    public AcademyController(
        ICourseCatalogProvider catalog,
        IEnrollmentService enrollments,
        ICertificateService certificates,
        IPriceFormatter priceFormatter,
        IMemberAccessGate gate)
    {
        _catalog = catalog;
        _enrollments = enrollments;
        _certificates = certificates;
        _priceFormatter = priceFormatter;
        _gate = gate;
    }


    // ── Identidad server-trusted (molde de Tienda/Gobierno/Eventos/Blogs/Propiedades) ──
    //
    // El expediente académico del alumno se tomaba de un `?student=` / `request.Student`.
    // Leer el progreso ajeno ya era un IDOR; ESCRIBIRLO era peor: `POST /progress` marcaba
    // lecciones como completadas en el expediente de otro. Un registro académico que
    // cualquiera puede escribir no registra nada.

    /// <summary>Rol(es) del panel de autoría de cursos.</summary>
    private const string InstructorRolesCsv = "instructor,admin";

    /// <summary>Exige sesión; devuelve el id server-trusted del alumno. 401 si es anónimo.</summary>
    private (IActionResult? denied, string studentId) RequireStudent()
    {
        var email = _gate.CurrentMemberEmail;
        if (!_gate.IsAuthenticated || string.IsNullOrWhiteSpace(email))
        {
            return (Unauthorized(new { error = "Se requiere iniciar sesión." }), string.Empty);
        }
        return (null, email);
    }

    /// <summary>
    /// Exige rol de INSTRUCTOR. 401 anónimo, 403 sin rol. Publicar un curso al catálogo del
    /// sitio y ver las métricas de matrícula no son cosas de cualquier alumno logueado.
    /// </summary>
    private (IActionResult? denied, string instructorId) RequireInstructor()
    {
        if (!_gate.IsAuthenticated)
        {
            return (Unauthorized(new { error = "Inicie sesión como instructor." }), string.Empty);
        }
        if (!_gate.HasAnyRole(InstructorRolesCsv))
        {
            // StatusCode(403), NO Forbid(): con auth de members Forbid redirige al login.
            return (StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Su cuenta no tiene permiso de instructor." }), string.Empty);
        }
        return (null, _gate.CurrentMemberEmail ?? string.Empty);
    }

    // ── 1. Courses (catálogo buscable) ─────────────────────────────────
    // GET /api/academy/courses?q=&category=&level= → { courses:[...] }
    [HttpGet("courses")]
    public async Task<IActionResult> Courses(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? level,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.SearchAsync(
            new CourseQuery(Text: q, Category: category, Level: level),
            cancellationToken);

        var courses = result.Courses.Select(ToCourseDto).ToList();
        return Ok(new CoursesResponse(Courses: courses, Total: result.Total));
    }

    // ── 2. Course detail (PDP-curso) ───────────────────────────────────
    // GET /api/academy/course/{id} → { course, modules:[{lessons:[...]}], instructor }
    [HttpGet("course/{id}")]
    public async Task<IActionResult> Course(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del curso es requerido." });
        }

        var detail = await _catalog.GetCourseAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Curso '{id}' no encontrado." });
        }

        var course = ToCourseDto(detail.Course) with
        {
            Description = detail.Description,
            Outcomes = detail.Outcomes,
        };

        var modules = detail.Modules.Select(m => new ModuleDto(
            Id: m.Id,
            Title: m.Title,
            Order: m.Order,
            Lessons: m.Lessons.Select(ToLessonDto).ToList())).ToList();

        var plans = detail.Plans.Select(p =>
        {
            // Cadena EMI que la UI lee en 'installments' (ej "3 x $172.800"); vacía en contado.
            var installmentString = p.Installments > 1
                ? $"{p.Installments} x {_priceFormatter.Format(decimal.Round(p.Total / p.Installments, 0, MidpointRounding.AwayFromZero), p.Currency)}"
                : string.Empty;
            return new PlanDto(
                Code: p.Code,
                Label: p.Label,
                Total: p.Total,
                Amount: p.Total,
                Price: p.Total,
                TotalFormatted: _priceFormatter.Format(p.Total, p.Currency),
                Currency: p.Currency,
                Installments: installmentString,
                InstallmentCount: p.Installments,
                InstallmentFormatted: p.Installments > 1
                    ? installmentString
                    : _priceFormatter.Format(p.Total, p.Currency));
        }).ToList();

        var instructor = new InstructorDto(
            Id: detail.Instructor.Id,
            Name: detail.Instructor.Name,
            Headline: detail.Instructor.Headline,
            Bio: detail.Instructor.Bio,
            AvatarUrl: detail.Instructor.AvatarUrl,
            Avatar: detail.Instructor.AvatarUrl);

        return Ok(new CourseDetailResponse(
            Course: course,
            Modules: modules,
            Instructor: instructor,
            Plans: plans));
    }

    // ── 3. Enroll ──────────────────────────────────────────────────────
    // POST /api/academy/enroll { courseId, student:{name,email} }
    //   → { orderRef, paymentSessionId, amount, currency } | { enrolled:true }
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll(
        [FromBody] EnrollRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CourseId))
        {
            return BadRequest(new { error = "courseId es requerido." });
        }
        if (request.Student is null
            || string.IsNullOrWhiteSpace(request.Student.Name)
            || string.IsNullOrWhiteSpace(request.Student.Email))
        {
            return BadRequest(new { error = "El alumno (name + email) es requerido." });
        }

        CourseEnrollmentResult result;
        try
        {
            result = await _enrollments.EnrollAsync(
                request.CourseId.Trim(),
                new Student(request.Student.Name.Trim(), request.Student.Email.Trim()),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // Rama gratis → { enrolled:true } (+ enrollmentId para desbloquear el aula).
        if (result.Enrolled)
        {
            return Ok(new EnrolledResponse(Enrolled: true, EnrollmentId: result.EnrollmentId));
        }

        // Rama de pago → { orderRef, paymentSessionId, amount, currency }.
        return Ok(new EnrollPaymentResponse(
            OrderRef: result.OrderRef!,
            PaymentSessionId: result.PaymentSessionId!,
            Amount: result.Amount,
            AmountFormatted: _priceFormatter.Format(result.Amount, result.Currency),
            Currency: result.Currency!));
    }

    // ── 4. Confirm ─────────────────────────────────────────────────────
    // POST /api/academy/confirm { orderRef } → { status, enrollmentId }
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmEnrollRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderRef))
        {
            return BadRequest(new { error = "orderRef es requerido." });
        }

        EnrollmentConfirmation result;
        try
        {
            result = await _enrollments.ConfirmAsync(request.OrderRef.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Pago no capturable — el cliente reintenta.
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ConfirmEnrollResponse(
            Status: result.Status,
            EnrollmentId: result.EnrollmentId,
            CourseId: result.CourseId));
    }

    // ── 5. Get progress ────────────────────────────────────────────────
    // GET /api/academy/progress?student=&course= → { completedLessonIds:[...], percent }
    // Acepta el nombre canónico del contrato (course/student) y el legacy (courseId)
    // que aún consume el módulo course-player — sin romper la UI existente.
    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress(
        [FromQuery] string? course,
        [FromQuery] string? courseId,
        CancellationToken cancellationToken)
    {
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        var courseKey = FirstNonEmpty(course, courseId);
        if (string.IsNullOrWhiteSpace(courseKey))
        {
            return BadRequest(new { error = "course es requerido." });
        }

        var progress = await _enrollments.GetProgressAsync(courseKey.Trim(), student.Trim(), cancellationToken);
        var certificate = await _certificates.GetAsync(student.Trim(), courseKey.Trim(), cancellationToken);

        return Ok(ToProgressDto(progress, certificate));
    }

    // ── 6. Post progress (marcar lección) ──────────────────────────────
    // POST /api/academy/progress { student, course, lesson } → { percent }
    // Acepta el shape canónico del contrato (course/lesson) y el legacy
    // (courseId/lessonId) que aún consume el módulo course-player.
    [HttpPost("progress")]
    public async Task<IActionResult> MarkProgress(
        [FromBody] MarkProgressRequest? request,
        CancellationToken cancellationToken)
    {
        var courseKey = request is null ? null : FirstNonEmpty(request.Course, request.CourseId);
        var lessonKey = request is null ? null : FirstNonEmpty(request.Lesson, request.LessonId);
        if (request is null
            || string.IsNullOrWhiteSpace(courseKey)
            || string.IsNullOrWhiteSpace(lessonKey))
        {
            return BadRequest(new { error = "course y lesson son requeridos." });
        }

        // El alumno sale del GATE: se IGNORA `request.Student`, que permitía marcar
        // lecciones completadas en el expediente de otro. El campo se deja en el DTO
        // por compatibilidad del cliente, pero ya no decide nada.
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        CourseProgress progress;
        try
        {
            progress = await _enrollments.MarkLessonAsync(
                courseKey.Trim(),
                lessonKey.Trim(),
                student,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var certificate = progress.Percent >= 100
            ? await _certificates.GetAsync(student, courseKey.Trim(), cancellationToken)
            : null;

        return Ok(ToProgressDto(progress, certificate));
    }

    // ── 7. Certificate (credencial verificable) ─────────────────────────
    // GET /api/academy/certificate?student=&course= → { certificate | null }
    [HttpGet("certificate")]
    public async Task<IActionResult> GetCertificate(
        [FromQuery] string? course,
        CancellationToken cancellationToken)
    {
        // Devuelve el certificado DEL ALUMNO LOGUEADO. Con `?student=` esto era un
        // padrón enumerable de quién estudió qué.
        //
        // Ojo con lo que este endpoint NO es: la verificación por un tercero (un
        // empleador). Esa es la promesa de `VerifyUrl`, y hoy NO existe ninguna ruta
        // que la cumpla. Cuando se construya, va por el ID de la credencial —que es
        // una capacidad, como el `orderRef` de travel—, nunca por el id del alumno.
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        if (string.IsNullOrWhiteSpace(course))
        {
            return BadRequest(new { error = "course es requerido." });
        }

        var certificate = await _certificates.GetAsync(student, course.Trim(), cancellationToken);
        return Ok(new CertificateResponse(
            Certificate: certificate is null
                ? null
                : new CertificateDto(certificate.Id, certificate.StudentName, certificate.IssuedAt, certificate.VerifyUrl)));
    }

    // ── 8. Instructor: sus cursos + métricas (panel de autor) ───────────
    // GET /api/academy/instructor/courses?instructor= → { courses:[...] }
    [HttpGet("instructor/courses")]
    public async Task<IActionResult> InstructorCourses(CancellationToken cancellationToken)
    {
        var (denied, instructor) = RequireInstructor();
        if (denied is not null) { return denied; }

        var result = await _catalog.GetForInstructorAsync(instructor, cancellationToken);

        var courses = result.Courses.Select(ic => new InstructorCourseDto(
            // studentCount real desde las métricas del panel (la card lo lee en course.studentCount).
            Course: ToCourseDto(ic.Course) with { StudentCount = ic.Metrics.Students },
            Students: ic.Metrics.Students,
            Revenue: ic.Metrics.Revenue,
            RevenueFormatted: _priceFormatter.Format(ic.Metrics.Revenue, ic.Metrics.Currency),
            Rating: ic.Metrics.Rating)).ToList();

        return Ok(new InstructorCoursesResponse(
            Instructor: result.InstructorId,
            Courses: courses,
            TotalStudents: result.TotalStudents,
            TotalRevenue: result.TotalRevenue,
            TotalRevenueFormatted: _priceFormatter.Format(result.TotalRevenue, result.Currency)));
    }

    // ── 9. Publish course (curriculum builder del instructor) ───────────
    // POST /api/academy/course { draft } → { courseId }
    [HttpPost("course")]
    public async Task<IActionResult> PublishCourse(
        [FromBody] CourseDraftRequest? request,
        CancellationToken cancellationToken)
    {
        // Publicar al catálogo del sitio era anónimo.
        var (denied, _) = RequireInstructor();
        if (denied is not null) { return denied; }

        if (request is null || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "El título del curso es requerido." });
        }
        if (request.Modules is null || request.Modules.Count == 0)
        {
            return BadRequest(new { error = "El curso requiere al menos un módulo." });
        }

        var draft = new CourseDraft(
            Title: request.Title,
            Summary: request.Summary ?? string.Empty,
            Description: request.Description ?? string.Empty,
            School: request.School ?? string.Empty,
            Category: request.Category ?? string.Empty,
            Level: request.Level ?? string.Empty,
            InstructorId: request.Instructor ?? request.InstructorId ?? string.Empty,
            Price: request.Price,
            Modules: request.Modules.Select(m => new CourseDraftModule(
                Title: m.Title ?? string.Empty,
                Lessons: (m.Lessons ?? new List<CourseDraftLessonRequest>()).Select(l => new CourseDraftLesson(
                    Title: l.Title ?? string.Empty,
                    VideoUrl: l.VideoUrl,
                    DurationMinutes: l.Duration,
                    ContentBody: l.ContentBody)).ToList())).ToList(),
            Outcomes: request.Outcomes,
            CoverImageUrl: request.CoverImageUrl);

        CourseDetail published;
        try
        {
            published = await _catalog.PublishCourseAsync(draft, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new PublishCourseResponse(CourseId: published.Course.Id));
    }

    private static string? FirstNonEmpty(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) ? a : b;

    // ── Helpers ────────────────────────────────────────────────────────

    private CourseDto ToCourseDto(CourseSummary c) => new(
        Id: c.Id,
        Title: c.Title,
        Summary: c.Summary,
        Category: c.Category,
        Level: MapLevel(c.Level),
        InstructorName: c.InstructorName,
        CoverImageUrl: c.CoverImageUrl,
        Cover: c.CoverImageUrl,
        Price: c.Price,
        PriceFormatted: c.IsFree ? "Gratis" : _priceFormatter.Format(c.Price, c.Currency),
        Currency: c.Currency,
        IsFree: c.IsFree,
        Rating: c.Rating,
        LessonCount: c.LessonCount,
        DurationMinutes: c.DurationMinutes,
        // studentCount no viene en el catálogo (CourseSummary); el panel de instructor lo
        // rellena desde métricas vía `with`. En el grid general queda null (dato ausente).
        StudentCount: null,
        Description: null,
        Outcomes: null);

    // Vocabulario de nivel que la UI lee: 'beginner' | 'intermediate' | 'advanced'.
    // La data del catálogo viene en español (Principiante/Intermedio/Avanzado).
    private static string MapLevel(string? level) => (level ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "avanzado" => "advanced",
        "intermedio" => "intermediate",
        _ => "beginner",
    };

    private static LessonDto ToLessonDto(CourseLesson l) => new(
        Id: l.Id,
        Title: l.Title,
        Order: l.Order,
        DurationMinutes: l.DurationMinutes,
        VideoRef: l.VideoRef,
        // ContentItemId = id del item Kind=lesson en el IContentStream (polimorfismo
        // Blogs): el course-player resuelve el cuerpo de la lección del MISMO feed.
        ContentItemId: l.ContentItemId,
        Resources: l.Resources.Select(r => new ResourceDto(r.Title, r.Url, r.Kind)).ToList(),
        IsPreview: l.IsPreview);

    private static ProgressResponse ToProgressDto(CourseProgress progress, Certificate? certificate) => new(
        CourseId: progress.CourseId,
        CompletedLessonIds: progress.CompletedLessonIds,
        Percent: progress.Percent,
        LastLessonId: progress.LastLessonId,
        Completed: progress.Percent >= 100,
        Certificate: certificate is null
            ? null
            : new CertificateDto(certificate.Id, certificate.StudentName, certificate.IssuedAt, certificate.VerifyUrl));

    // ── Request DTOs (binding de los módulos course-catalog + course-player) ──

    /// <summary>El alumno en el payload de enroll.</summary>
    public sealed record StudentRequest(string Name, string Email);

    /// <summary>POST /api/academy/enroll — curso + alumno.</summary>
    public sealed record EnrollRequest(string CourseId, StudentRequest? Student);

    /// <summary>POST /api/academy/confirm — la inscripción a capturar.</summary>
    public sealed record ConfirmEnrollRequest(string OrderRef);

    /// <summary>
    /// POST /api/academy/progress — la lección a marcar completa. El contrato
    /// canónico usa <c>student/course/lesson</c>; se conservan <c>courseId/lessonId</c>
    /// (nullable) para el módulo course-player existente. El controller toma el
    /// primero no vacío de cada par.
    /// </summary>
    public sealed record MarkProgressRequest(
        string Student,
        string? Course = null,
        string? Lesson = null,
        string? CourseId = null,
        string? LessonId = null);

    /// <summary>POST /api/academy/course — borrador que publica el instructor (curriculum builder).</summary>
    public sealed record CourseDraftRequest(
        string Title,
        string? Summary = null,
        string? Description = null,
        string? School = null,
        string? Category = null,
        string? Level = null,
        string? Instructor = null,
        string? InstructorId = null,
        decimal Price = 0m,
        IReadOnlyList<CourseDraftModuleRequest>? Modules = null,
        IReadOnlyList<string>? Outcomes = null,
        string? CoverImageUrl = null);

    /// <summary>Un módulo del borrador del curso (título + lecciones).</summary>
    public sealed record CourseDraftModuleRequest(
        string? Title,
        List<CourseDraftLessonRequest>? Lessons);

    /// <summary>Una lección del borrador (título + video + duración).</summary>
    public sealed record CourseDraftLessonRequest(
        string? Title,
        string? VideoUrl = null,
        int Duration = 0,
        string? ContentBody = null);

    // ── Response DTOs (JSON estable para la UI) ────────────────────────

    public sealed record CourseDto(
        string Id,
        string Title,
        string Summary,
        string Category,
        string Level,
        string InstructorName,
        string? CoverImageUrl,
        string? Cover,
        decimal Price,
        string PriceFormatted,
        string Currency,
        bool IsFree,
        double Rating,
        int LessonCount,
        int DurationMinutes,
        int? StudentCount,
        string? Description,
        IReadOnlyList<string>? Outcomes);

    public sealed record CoursesResponse(IReadOnlyList<CourseDto> Courses, int Total);

    public sealed record ResourceDto(string Title, string Url, string Kind);

    public sealed record LessonDto(
        string Id,
        string Title,
        int Order,
        int DurationMinutes,
        string? VideoRef,
        string ContentItemId,
        IReadOnlyList<ResourceDto> Resources,
        bool IsPreview);

    public sealed record ModuleDto(
        string Id,
        string Title,
        int Order,
        IReadOnlyList<LessonDto> Lessons);

    public sealed record InstructorDto(
        string Id,
        string Name,
        string Headline,
        string Bio,
        string? AvatarUrl,
        string? Avatar);

    public sealed record PlanDto(
        string Code,
        string Label,
        decimal Total,
        decimal Amount,
        decimal Price,
        string TotalFormatted,
        string Currency,
        string Installments,
        int InstallmentCount,
        string InstallmentFormatted);

    public sealed record CourseDetailResponse(
        CourseDto Course,
        IReadOnlyList<ModuleDto> Modules,
        InstructorDto Instructor,
        IReadOnlyList<PlanDto> Plans);

    public sealed record EnrolledResponse(bool Enrolled, string? EnrollmentId);

    public sealed record EnrollPaymentResponse(
        string OrderRef,
        string PaymentSessionId,
        decimal Amount,
        string AmountFormatted,
        string Currency);

    public sealed record ConfirmEnrollResponse(string Status, string EnrollmentId, string CourseId);

    public sealed record CertificateDto(string Id, string StudentName, DateTimeOffset IssuedAt, string VerifyUrl);

    public sealed record ProgressResponse(
        string CourseId,
        IReadOnlyList<string> CompletedLessonIds,
        int Percent,
        string? LastLessonId,
        bool Completed,
        CertificateDto? Certificate);

    /// <summary>GET /api/academy/certificate — { certificate | null }.</summary>
    public sealed record CertificateResponse(CertificateDto? Certificate);

    /// <summary>Una fila del panel del instructor: el curso + sus métricas (alumnos/ingresos/rating).</summary>
    public sealed record InstructorCourseDto(
        CourseDto Course,
        int Students,
        decimal Revenue,
        string RevenueFormatted,
        double Rating);

    /// <summary>GET /api/academy/instructor/courses — { courses:[...] } + totales del panel.</summary>
    public sealed record InstructorCoursesResponse(
        string Instructor,
        IReadOnlyList<InstructorCourseDto> Courses,
        int TotalStudents,
        decimal TotalRevenue,
        string TotalRevenueFormatted);

    /// <summary>POST /api/academy/course — { courseId } del curso publicado.</summary>
    public sealed record PublishCourseResponse(string CourseId);
}
