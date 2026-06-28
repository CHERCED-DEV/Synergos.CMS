using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IEnrollmentService"/> — motor de matrícula + progreso del
/// LMS (dominio Educación) server-side liviano, calcando <c>StubShopOrderService</c>
/// (Tienda) y <c>StubReservationService</c> (Hoteles). Compone el catálogo
/// (<see cref="ICourseCatalogProvider"/>, para resolver el precio real —
/// anti-tampering — y el total de lecciones) y el pago
/// (<see cref="IPaymentProvider"/>), y lleva la inscripción por el flujo
/// unificado enroll → pagar → confirmar. Añade lo propio del LMS: progreso por
/// lección + certificado al 100%.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). El precio NUNCA se confía al cliente: se
/// resuelve desde el catálogo en enroll. La rama de pago abre UNA sesión
/// (<see cref="IPaymentProvider"/>); la rama gratis crea la matrícula Active de
/// inmediato. Estado (orderRef → matrícula; (alumno,curso) → progreso) en memoria
/// del proceso, suficiente para demo; un adapter real delega a DB. ConfirmAsync y
/// MarkLessonAsync son idempotentes. ADR 0075 (seam con tests canónicos).
/// </remarks>
public sealed class StubEnrollmentService : IEnrollmentService
{
    private readonly ICourseCatalogProvider _catalog;
    private readonly IPaymentProvider _payments;
    private readonly Func<DateTimeOffset> _now;

    // Matrículas por orderRef (rama de pago) y por enrollmentId (acceso directo).
    private readonly ConcurrentDictionary<string, EnrollmentState> _byOrderRef = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EnrollmentState> _byEnrollmentId = new(StringComparer.Ordinal);

    // Progreso por clave (alumno|curso) → set de lecciones completadas + última.
    private readonly ConcurrentDictionary<string, ProgressState> _progress = new(StringComparer.Ordinal);

    public StubEnrollmentService(ICourseCatalogProvider catalog, IPaymentProvider payments)
        : this(catalog, payments, null)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable (<paramref name="now"/>) para
    /// determinismo en tests (ADR 0002). Null = reloj real.
    /// </summary>
    public StubEnrollmentService(ICourseCatalogProvider catalog, IPaymentProvider payments, Func<DateTimeOffset>? now)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CourseEnrollmentResult> EnrollAsync(string courseId, Student student, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(student);
        if (string.IsNullOrWhiteSpace(student.Name) || string.IsNullOrWhiteSpace(student.Email))
        {
            throw new ArgumentException("El nombre y el email del alumno son obligatorios.", nameof(student));
        }

        // Precio REAL desde el catálogo (no se confía en el cliente).
        var detail = await _catalog.GetCourseAsync(courseId, cancellationToken);
        if (detail is null)
        {
            throw new ArgumentException($"Curso '{courseId}' no encontrado.", nameof(courseId));
        }

        var course = detail.Course;
        var studentName = student.Name.Trim();
        var studentEmail = student.Email.Trim();

        // Rama gratis: matrícula Active inmediata, sin sesión de pago.
        if (course.IsFree)
        {
            var freeEnrollment = CreateEnrollment(
                course.Id, studentName, studentEmail, EnrollmentStatus.Active,
                orderRef: null, paymentSessionId: null, total: 0m, currency: course.Currency);
            _byEnrollmentId[freeEnrollment.EnrollmentId] = freeEnrollment;
            return new CourseEnrollmentResult(
                Enrolled: true,
                EnrollmentId: freeEnrollment.EnrollmentId,
                Currency: course.Currency);
        }

        // Rama de pago: abre UNA sesión por el precio del curso; matrícula PendingPayment.
        var orderRef = $"enr_{Guid.NewGuid():N}";
        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: orderRef,
                Amount: course.Price,
                Currency: course.Currency,
                Items: new[]
                {
                    new PaymentLineItem(
                        Sku: course.Id,
                        Description: $"Inscripción: {course.Title}",
                        UnitPrice: course.Price,
                        Quantity: 1),
                },
                CustomerEmail: studentEmail,
                ReturnUrl: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["courseId"] = course.Id,
                }),
            cancellationToken);

        var pending = CreateEnrollment(
            course.Id, studentName, studentEmail, EnrollmentStatus.PendingPayment,
            orderRef: orderRef, paymentSessionId: session.SessionId, total: course.Price, currency: course.Currency);
        _byOrderRef[orderRef] = pending;
        _byEnrollmentId[pending.EnrollmentId] = pending;

        return new CourseEnrollmentResult(
            Enrolled: false,
            OrderRef: orderRef,
            PaymentSessionId: session.SessionId,
            Amount: course.Price,
            Currency: course.Currency,
            EnrollmentId: pending.EnrollmentId);
    }

    public async Task<EnrollmentConfirmation> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef) || !_byOrderRef.TryGetValue(orderRef, out var enrollment))
        {
            throw new ArgumentException("Inscripción no encontrada.", nameof(orderRef));
        }

        // Idempotente: si ya está activa, devolver la matrícula sin recapturar.
        if (enrollment.Status == EnrollmentStatus.Active)
        {
            return ToConfirmation(enrollment);
        }

        // Captura el pago (idempotente en el PSP). Si no captura → no se activa.
        var capture = await _payments.CaptureAsync(enrollment.PaymentSessionId!, cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago de la inscripción (estado {capture.Status}).");
        }

        var active = enrollment with { Status = EnrollmentStatus.Active };
        _byOrderRef[orderRef] = active;
        _byEnrollmentId[active.EnrollmentId] = active;
        return ToConfirmation(active);
    }

    public async Task<CourseProgress> GetProgressAsync(string courseId, string student, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(student))
        {
            return new CourseProgress(courseId ?? string.Empty, Array.Empty<string>(), 0);
        }

        var totalLessons = await ResolveTotalLessonsAsync(courseId, cancellationToken);
        return BuildProgress(courseId, student, totalLessons);
    }

    public async Task<CourseProgress> MarkLessonAsync(string courseId, string lessonId, string student, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(lessonId) || string.IsNullOrWhiteSpace(student))
        {
            throw new ArgumentException("courseId, lessonId y student son obligatorios.");
        }

        // Valida que el curso y la lección existan en el catálogo (anti-garbage).
        var detail = await _catalog.GetCourseAsync(courseId, cancellationToken);
        if (detail is null)
        {
            throw new ArgumentException($"Curso '{courseId}' no encontrado.", nameof(courseId));
        }
        var allLessons = detail.Modules.SelectMany(m => m.Lessons).ToList();
        if (!allLessons.Any(l => string.Equals(l.Id, lessonId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Lección '{lessonId}' no encontrada en el curso '{courseId}'.", nameof(lessonId));
        }

        var key = ProgressKey(courseId, student);
        // Idempotente: AddOrUpdate con HashSet — marcar dos veces no duplica.
        _progress.AddOrUpdate(
            key,
            _ =>
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { lessonId };
                return new ProgressState(set, lessonId);
            },
            (_, existing) =>
            {
                existing.Completed.Add(lessonId);
                return existing with { LastLessonId = lessonId };
            });

        return BuildProgress(courseId, student, allLessons.Count);
    }

    public async Task<Certificate?> GetCertificateAsync(string courseId, string student, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(student))
        {
            return null;
        }

        var detail = await _catalog.GetCourseAsync(courseId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var totalLessons = detail.Modules.Sum(m => m.Lessons.Count);
        var progress = BuildProgress(courseId, student, totalLessons);
        if (progress.Percent < 100)
        {
            return null; // El certificado solo se emite al 100%.
        }

        // Id estable derivado de (curso,alumno) → re-emitir devuelve el mismo
        // certificado (idempotente). El nombre del alumno sale del progreso si
        // hay matrícula; si no, del propio identificador.
        var certId = "cert-" + StableHash($"{courseId}|{student}");
        var studentName = ResolveStudentName(student);

        return new Certificate(
            Id: certId,
            CourseId: courseId,
            StudentName: studentName,
            IssuedAt: _now(),
            VerifyUrl: $"/academy/verify/{certId}");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private EnrollmentState CreateEnrollment(
        string courseId, string studentName, string studentEmail, EnrollmentStatus status,
        string? orderRef, string? paymentSessionId, decimal total, string currency)
        => new(
            EnrollmentId: $"enrl_{Guid.NewGuid():N}",
            CourseId: courseId,
            StudentName: studentName,
            StudentEmail: studentEmail,
            Status: status,
            OrderRef: orderRef,
            PaymentSessionId: paymentSessionId,
            Total: total,
            Currency: currency,
            CreatedAt: _now());

    private async Task<int> ResolveTotalLessonsAsync(string courseId, CancellationToken cancellationToken)
    {
        var detail = await _catalog.GetCourseAsync(courseId, cancellationToken);
        return detail?.Modules.Sum(m => m.Lessons.Count) ?? 0;
    }

    private CourseProgress BuildProgress(string courseId, string student, int totalLessons)
    {
        if (!_progress.TryGetValue(ProgressKey(courseId, student), out var state) || state.Completed.Count == 0)
        {
            return new CourseProgress(courseId, Array.Empty<string>(), 0);
        }

        // El % se calcula sobre el total de lecciones del curso; las lecciones
        // completadas que ya no existen en el catálogo no inflan el numerador.
        var completed = state.Completed.ToList();
        var percent = totalLessons <= 0
            ? 0
            : (int)Math.Round(Math.Min(completed.Count, totalLessons) * 100.0 / totalLessons, MidpointRounding.AwayFromZero);

        return new CourseProgress(courseId, completed, percent, state.LastLessonId);
    }

    private string ResolveStudentName(string student)
    {
        // Si alguna matrícula del alumno (por email) tiene nombre, úsalo.
        var match = _byEnrollmentId.Values.FirstOrDefault(e =>
            string.Equals(e.StudentEmail, student, StringComparison.OrdinalIgnoreCase));
        return match?.StudentName ?? student;
    }

    private static EnrollmentConfirmation ToConfirmation(EnrollmentState e)
        => new(e.Status.ToString(), e.EnrollmentId, e.CourseId);

    private static string ProgressKey(string courseId, string student)
        => $"{student.Trim().ToLowerInvariant()}|{courseId.Trim().ToLowerInvariant()}";

    // FNV-1a 32-bit (forzado positivo) → id de certificado estable y determinista.
    private static string StableHash(string value)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }
        return (hash & 0x7FFFFFFF).ToString("x8");
    }

    private sealed record EnrollmentState(
        string EnrollmentId,
        string CourseId,
        string StudentName,
        string StudentEmail,
        EnrollmentStatus Status,
        string? OrderRef,
        string? PaymentSessionId,
        decimal Total,
        string Currency,
        DateTimeOffset CreatedAt);

    private sealed record ProgressState(HashSet<string> Completed, string? LastLessonId);
}
