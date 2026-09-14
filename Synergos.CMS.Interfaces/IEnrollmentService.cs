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
/// Una matrícula ACTIVA del alumno, con su avance. Es la fila de «mi aprendizaje».
/// </summary>
/// <remarks>
/// <b>Junta matrícula y progreso a propósito</b>: por separado obligarían a quien las pinte a
/// pedir el progreso curso por curso, y esa pantalla lista todos los del alumno de una.
/// <see cref="LastActivityAt"/> es la fecha de la matrícula mientras nadie registre la última
/// lección vista — está declarado aquí para que quien lo lea no lo tome por lo que no es.
/// </remarks>
public sealed record StudentEnrollment(
    string EnrollmentId,
    string CourseId,
    int Percent,
    int CompletedCount,
    DateTimeOffset LastActivityAt);

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
    /// curso no existe, el alumno es inválido o el plan no existe.
    /// </summary>
    /// <param name="planCode">
    /// El plan que eligió el alumno (<see cref="CoursePricingPlan.Code"/>), o null para el
    /// precio del curso.
    /// </param>
    /// <remarks>
    /// <b>Llega el CÓDIGO del plan, nunca su monto</b>, y eso es lo mismo que hace el curso:
    /// el total se resuelve desde el catálogo. Aceptar el monto dejaría que quien llama se
    /// ponga el precio que quiera — es el «no se confía en el cliente» de arriba aplicado a la
    /// otra mitad de la decisión.
    ///
    /// <para><b>Un código que no existe se RECHAZA, no cae al precio del curso</b> (defecto
    /// #102). Caer es exactamente el defecto que esto viene a arreglar con otro nombre: el
    /// alumno elige «Plan Premium con mentoría», se le cobra el precio líder y nada falla. Es
    /// plata y se nota al instante —falla delante de quien está comprando y se arregla
    /// eligiendo otra vez—, así que falla a la vista.</para>
    /// </remarks>
    Task<CourseEnrollmentResult> EnrollAsync(
        string courseId,
        Student student,
        string? planCode = null,
        CancellationToken cancellationToken = default);

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
    /// Todas las matrículas ACTIVAS del alumno con su avance. Sin ninguna devuelve <c>[]</c>.
    /// </summary>
    /// <remarks>
    /// <b>Faltaba, y por eso «mi aprendizaje» no se podía servir</b> (#102). El seam sabía
    /// contestar «¿cómo va este alumno en ESTE curso?» (<see cref="GetProgressAsync"/>), que
    /// obliga a conocer el curso de antemano; la pantalla que lista lo que alguien está
    /// cursando necesita la pregunta al revés.
    ///
    /// <para><b>Sólo las ACTIVAS</b>: una matrícula en <c>PendingPayment</c> es un carrito
    /// abandonado, y ponerla entre «mis cursos» le diría al alumno que tiene acceso a algo que
    /// no pagó.</para>
    /// </remarks>
    Task<IReadOnlyList<StudentEnrollment>> GetEnrollmentsAsync(string student, CancellationToken cancellationToken = default);

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

/// <summary>Métricas de matrícula agregadas de un curso: cuántos alumnos y cuánto ingreso acumulado.</summary>
public sealed record CourseEnrollmentStats(int Students, decimal Revenue);

/// <summary>
/// Cara de LECTURA del motor de matrícula para el panel del instructor
/// (performance/revenue) — seam ISP-clean separado del flujo transaccional de
/// <see cref="IEnrollmentService"/>. Lo COMPONE el catálogo para armar las
/// métricas por curso sin duplicar el estado de las matrículas (DIP): el catálogo
/// depende de esta abstracción, no del store de matrículas. Opcional en la
/// construcción del catálogo (null = métricas en cero) para no crear un ciclo de
/// dependencias en el composer.
/// </summary>
public interface IEnrollmentMetrics
{
    /// <summary>
    /// Devuelve las métricas de matrícula de un curso (alumnos activos + ingreso
    /// acumulado de las matrículas de pago). Curso sin matrículas → ceros; nunca lanza.
    /// </summary>
    Task<CourseEnrollmentStats> GetCourseStatsAsync(string courseId, CancellationToken cancellationToken = default);
}
