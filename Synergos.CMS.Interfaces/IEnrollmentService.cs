namespace Synergos.CMS.Interfaces;

/// <summary>
/// Ciclo de vida de una matrícula del dominio Educación (LMS). Calca
/// <see cref="ReservationStatus"/> de Hoteles, pero el "Held" es una
/// inscripción pendiente de pago y el "Confirmed/Active" es matrícula activa
/// (acceso al aula desbloqueado). app-spec §4.
/// </summary>
public enum EnrollmentStatus
{
    /// <summary>Inscripción creada, esperando captura del pago (≈ Held).</summary>
    PendingPayment,
    /// <summary>Matrícula activa (pago capturado o curso gratis) — aula desbloqueada.</summary>
    Active,
    /// <summary>Cancelada / reembolsada.</summary>
    Cancelled,
}

/// <summary>El alumno que se inscribe (un Member en prod; proyección liviana aquí).</summary>
public sealed record Student(string Name, string Email);

/// <summary>
/// Resultado de <see cref="IEnrollmentService.EnrollAsync"/>. Polimórfico por
/// rama:
/// <list type="bullet">
/// <item>Curso de pago → <see cref="OrderRef"/> + <see cref="PaymentSessionId"/>
///   + <see cref="Amount"/>/<see cref="Currency"/> (la UI cobra y luego llama
///   Confirm). <see cref="Enrolled"/> = false.</item>
/// <item>Curso gratis → <see cref="Enrolled"/> = true + <see cref="EnrollmentId"/>
///   (matrícula activa inmediata, sin sesión de pago).</item>
/// </list>
/// El nombre lleva el prefijo <c>Course</c> para no colisionar con el enum
/// <c>EnrollmentResult</c> del 2FA (Member TOTP), no relacionado.
/// </summary>
public sealed record CourseEnrollmentResult(
    bool Enrolled,
    string? OrderRef = null,
    string? PaymentSessionId = null,
    decimal Amount = 0m,
    string? Currency = null,
    string? EnrollmentId = null);

/// <summary>
/// Resultado de <see cref="IEnrollmentService.ConfirmAsync"/>: estado de la
/// matrícula tras capturar el pago + el id de la matrícula creada.
/// <see cref="Status"/> es <c>Active</c> si la captura tuvo éxito.
/// </summary>
public sealed record EnrollmentConfirmation(
    string Status,
    string EnrollmentId,
    string CourseId);

/// <summary>
/// Progreso de un alumno en un curso: las lecciones completadas + el % de
/// avance. Habilita "continuar donde quedaste" y la barra de progreso del aula
/// (app-spec §2). <see cref="LastLessonId"/> = la última lección marcada.
/// </summary>
public sealed record CourseProgress(
    string CourseId,
    IReadOnlyList<string> CompletedLessonIds,
    int Percent,
    string? LastLessonId = null);

/// <summary>
/// Certificado emitido al completar un curso al 100% (app-spec §2/§4). Stub:
/// id + URL de verificación (render con qr-code); prod: firma/registro verificable.
/// </summary>
public sealed record Certificate(
    string Id,
    string CourseId,
    string StudentName,
    DateTimeOffset IssuedAt,
    string VerifyUrl);

/// <summary>
/// Servicio de matrícula + progreso del dominio Educación (LMS). Es el MOTOR
/// transaccional del enrollment, calcando <see cref="IReservationService"/> de
/// Hoteles (Hold→pay→Confirm) y <see cref="IShopOrderService"/> de Tienda:
/// compone el catálogo (<see cref="ICourseCatalogProvider"/>, para resolver el
/// precio real — anti-tampering) + el pago (<see cref="IPaymentProvider"/>).
/// Añade lo propio del LMS: progreso por lección + certificado al 100%.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que el resto del motor): el default
/// <c>StubEnrollmentService</c> (Application, lógica pura) mantiene el estado en
/// memoria (matrículas + progreso por (alumno,curso)) para que la demo corra
/// end-to-end; el adapter real (DB de matrículas) se enchufa sin tocar el motor.
/// <see cref="ConfirmAsync"/> y <see cref="MarkLessonAsync"/> son idempotentes.
/// ADR 0002 (Application sin Umbraco) + ADR 0075 (seam con tests).
/// </remarks>
public interface IEnrollmentService
{
    /// <summary>
    /// Inscribe a un alumno en un curso. Resuelve el precio real del curso
    /// desde el catálogo (no se confía en el cliente). Si el curso es de pago,
    /// abre UNA sesión de pago (<see cref="IPaymentProvider"/>) y devuelve la
    /// orden + sesión + monto (matrícula <see cref="EnrollmentStatus.PendingPayment"/>).
    /// Si es gratis, crea la matrícula <see cref="EnrollmentStatus.Active"/>
    /// inmediatamente (sin pago). Lanza <see cref="ArgumentException"/> si el
    /// curso no existe o el alumno es inválido.
    /// </summary>
    Task<CourseEnrollmentResult> EnrollAsync(string courseId, Student student, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captura el pago de una inscripción pendiente y activa la matrícula.
    /// Idempotente: re-confirmar el mismo <paramref name="orderRef"/> devuelve la
    /// misma matrícula sin doble captura. Lanza <see cref="ArgumentException"/> si
    /// el orderRef no existe e <see cref="InvalidOperationException"/> si el pago
    /// no se pudo capturar.
    /// </summary>
    Task<EnrollmentConfirmation> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el progreso del alumno en el curso (lecciones completadas + %).
    /// Sin progreso previo (o alumno no matriculado) devuelve 0% + lista vacía
    /// — nunca lanza por estado vacío.
    /// </summary>
    Task<CourseProgress> GetProgressAsync(string courseId, string student, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca una lección como completada y recalcula el % sobre el total de
    /// lecciones del curso (resuelto del catálogo). Idempotente: marcar dos veces
    /// la misma lección no la duplica ni infla el %. Devuelve el progreso actualizado.
    /// Lanza <see cref="ArgumentException"/> si el curso o la lección no existen.
    /// </summary>
    Task<CourseProgress> MarkLessonAsync(string courseId, string lessonId, string student, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite el certificado del curso para el alumno cuando su progreso = 100%.
    /// Idempotente: re-emitir devuelve el mismo certificado (id estable). Devuelve
    /// <c>null</c> si el alumno aún no completó el curso al 100%.
    /// </summary>
    Task<Certificate?> GetCertificateAsync(string courseId, string student, CancellationToken cancellationToken = default);
}
