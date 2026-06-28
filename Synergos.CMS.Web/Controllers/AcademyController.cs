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
    private readonly IPriceFormatter _priceFormatter;

    public AcademyController(
        ICourseCatalogProvider catalog,
        IEnrollmentService enrollments,
        IPriceFormatter priceFormatter)
    {
        _catalog = catalog;
        _enrollments = enrollments;
        _priceFormatter = priceFormatter;
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

        var plans = detail.Plans.Select(p => new PlanDto(
            Code: p.Code,
            Label: p.Label,
            Total: p.Total,
            TotalFormatted: _priceFormatter.Format(p.Total, p.Currency),
            Currency: p.Currency,
            Installments: p.Installments,
            InstallmentFormatted: p.Installments > 1
                ? $"{p.Installments} x {_priceFormatter.Format(decimal.Round(p.Total / p.Installments, 0, MidpointRounding.AwayFromZero), p.Currency)}"
                : _priceFormatter.Format(p.Total, p.Currency))).ToList();

        var instructor = new InstructorDto(
            Id: detail.Instructor.Id,
            Name: detail.Instructor.Name,
            Headline: detail.Instructor.Headline,
            Bio: detail.Instructor.Bio,
            AvatarUrl: detail.Instructor.AvatarUrl);

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
    // GET /api/academy/progress?courseId=&student= → { completedLessonIds:[...], percent }
    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress(
        [FromQuery] string? courseId,
        [FromQuery] string? student,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(student))
        {
            return BadRequest(new { error = "courseId y student son requeridos." });
        }

        var progress = await _enrollments.GetProgressAsync(courseId.Trim(), student.Trim(), cancellationToken);
        var certificate = await _enrollments.GetCertificateAsync(courseId.Trim(), student.Trim(), cancellationToken);

        return Ok(ToProgressDto(progress, certificate));
    }

    // ── 6. Post progress (marcar lección) ──────────────────────────────
    // POST /api/academy/progress { courseId, lessonId, student } → { percent }
    [HttpPost("progress")]
    public async Task<IActionResult> MarkProgress(
        [FromBody] MarkProgressRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.CourseId)
            || string.IsNullOrWhiteSpace(request.LessonId)
            || string.IsNullOrWhiteSpace(request.Student))
        {
            return BadRequest(new { error = "courseId, lessonId y student son requeridos." });
        }

        CourseProgress progress;
        try
        {
            progress = await _enrollments.MarkLessonAsync(
                request.CourseId.Trim(),
                request.LessonId.Trim(),
                request.Student.Trim(),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var certificate = progress.Percent >= 100
            ? await _enrollments.GetCertificateAsync(request.CourseId.Trim(), request.Student.Trim(), cancellationToken)
            : null;

        return Ok(ToProgressDto(progress, certificate));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private CourseDto ToCourseDto(CourseSummary c) => new(
        Id: c.Id,
        Title: c.Title,
        Summary: c.Summary,
        Category: c.Category,
        Level: c.Level,
        InstructorName: c.InstructorName,
        CoverImageUrl: c.CoverImageUrl,
        Price: c.Price,
        PriceFormatted: c.IsFree ? "Gratis" : _priceFormatter.Format(c.Price, c.Currency),
        Currency: c.Currency,
        IsFree: c.IsFree,
        Rating: c.Rating,
        LessonCount: c.LessonCount,
        DurationMinutes: c.DurationMinutes,
        Description: null,
        Outcomes: null);

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

    /// <summary>POST /api/academy/progress — la lección a marcar completa.</summary>
    public sealed record MarkProgressRequest(string CourseId, string LessonId, string Student);

    // ── Response DTOs (JSON estable para la UI) ────────────────────────

    public sealed record CourseDto(
        string Id,
        string Title,
        string Summary,
        string Category,
        string Level,
        string InstructorName,
        string? CoverImageUrl,
        decimal Price,
        string PriceFormatted,
        string Currency,
        bool IsFree,
        double Rating,
        int LessonCount,
        int DurationMinutes,
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
        string? AvatarUrl);

    public sealed record PlanDto(
        string Code,
        string Label,
        decimal Total,
        string TotalFormatted,
        string Currency,
        int Installments,
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
}
