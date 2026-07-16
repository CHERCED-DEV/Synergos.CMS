using System.Text.Encodings.Web;
using System.Text.Json;
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
/// inmediato. ConfirmAsync y MarkLessonAsync son idempotentes. ADR 0075.
/// <para>
/// <b>Durabilidad (doc 25 · ADR 0105):</b> el estado ya NO vive en diccionarios
/// del proceso sino detrás del seam <see cref="IJsonEntityStore"/> — con un
/// adapter FileSystem la matrícula y el progreso SOBREVIVEN un reinicio del CMS.
/// Dos familias: <c>"enrollments"</c> (una entrada por matrícula, keyed por
/// <see cref="PersistedEnrollment.EnrollmentId"/> — SIEMPRE presente, también en
/// la rama gratis donde no hay orderRef; el lookup por orderRef filtra sobre
/// <c>ListAsync</c>, O(n) aceptable al volumen del motor y sin DUPLICAR el estado
/// en dos índices que se desincronizarían) y <c>"course-progress"</c> (una entrada
/// por (alumno,curso), keyed por la MISMA clave compuesta determinista que usaba
/// el índice en memoria, saneada para el sistema de archivos).
/// </para>
/// </remarks>
public sealed class StubEnrollmentService : IEnrollmentService, IEnrollmentMetrics
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    /// <summary>Familia de matrículas en el store genérico (→ App_Data/syn-enrollments/).</summary>
    private const string EnrollmentResourceType = "enrollments";

    /// <summary>Familia de progreso por (alumno,curso) (→ App_Data/syn-course-progress/).</summary>
    private const string ProgressResourceType = "course-progress";

    /// <summary>Etapa inicial del pipeline de aprendizaje — se siembra al activar la matrícula.</summary>
    public const string StageEnrolled = "enrolled";

    /// <summary>
    /// Pipeline del ciclo de aprendizaje (LMS): matriculado → en progreso →
    /// completado. Instancia PROPIA del seam genérico <see cref="IOrderTrackingService"/>
    /// — NO reusa el pipeline de Tienda (pago→envío→entrega). Al activar la
    /// matrícula avanza a "enrolled"; la primera lección marcada, a "in-progress"; el
    /// 100%, a "completed".
    /// </summary>
    public static readonly IReadOnlyList<OrderTrackingStageDefinition> AcademyPipeline = new[]
    {
        new OrderTrackingStageDefinition(StageEnrolled, "Matriculado"),
        new OrderTrackingStageDefinition("in-progress", "En progreso"),
        new OrderTrackingStageDefinition("completed", "Completado"),
    };

    private readonly ICourseCatalogProvider _catalog;
    private readonly IPaymentProvider _payments;
    private readonly IOrderTrackingService? _tracking;
    private readonly IJsonEntityStore _store;
    private readonly ITransactionalNotifier? _notifier;
    private readonly Func<DateTimeOffset> _now;

    public StubEnrollmentService(ICourseCatalogProvider catalog, IPaymentProvider payments)
        : this(catalog, payments, null, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable (<paramref name="now"/>) para
    /// determinismo en tests (ADR 0002). Null = reloj real.
    /// </summary>
    public StubEnrollmentService(ICourseCatalogProvider catalog, IPaymentProvider payments, Func<DateTimeOffset>? now)
        : this(catalog, payments, null, null, now)
    {
    }

    /// <summary>
    /// Ctor con tracking: además del catálogo y el pago, recibe el
    /// <see cref="IOrderTrackingService"/> del pipeline de aprendizaje
    /// (<see cref="AcademyPipeline"/>) que la matrícula ALIMENTA (enrolled →
    /// in-progress → completed). Null = sin tracking (el motor sigue funcionando).
    /// Persistencia en memoria por default.
    /// </summary>
    public StubEnrollmentService(
        ICourseCatalogProvider catalog,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        Func<DateTimeOffset>? now)
        : this(catalog, payments, tracking, null, now)
    {
    }

    /// <summary>
    /// Ctor completo (durabilidad): <paramref name="store"/> es el backing store
    /// de matrículas + progreso (FileSystem en Web → sobrevive reinicio; InMemory
    /// en tests). Null = <see cref="InMemoryJsonEntityStore"/> (comportamiento
    /// idéntico al de los diccionarios que reemplazó). <paramref name="notifier"/> (T4)
    /// es el seam opcional que avisa al alumno cuando su matrícula queda activa; null =
    /// sin notificaciones (comportamiento pre-T4). Va ÚLTIMO y opcional a propósito: los
    /// factories del composer pasan args posicionales.
    /// </summary>
    public StubEnrollmentService(
        ICourseCatalogProvider catalog,
        IPaymentProvider payments,
        IOrderTrackingService? tracking,
        IJsonEntityStore? store,
        Func<DateTimeOffset>? now,
        ITransactionalNotifier? notifier = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _tracking = tracking;
        _store = store ?? new InMemoryJsonEntityStore();
        _notifier = notifier;
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
            await WriteEnrollmentAsync(freeEnrollment, cancellationToken);
            // Alimenta el timeline de aprendizaje: matrícula activa → "enrolled".
            await AdvanceTrackingAsync(freeEnrollment.EnrollmentId, StageEnrolled, cancellationToken);
            // T4: la rama gratis activa la matrícula AQUÍ y nunca pasa por ConfirmAsync
            // — si no se emitiera aquí, ningún alumno de curso gratis recibiría aviso.
            await NotifyActiveAsync(freeEnrollment, course.Title, cancellationToken);
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
        await WriteEnrollmentAsync(pending, cancellationToken);

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
        var enrollment = await LoadByOrderRefAsync(orderRef, cancellationToken);
        if (enrollment is null)
        {
            throw new ArgumentException("Inscripción no encontrada.", nameof(orderRef));
        }

        // Idempotente: si ya está activa, devolver la matrícula sin recapturar.
        if (enrollment.Status == EnrollmentStatus.Active)
        {
            // Re-emite: el ledger del dispatcher deduplica (un hecho → un aviso), así que
            // es inofensivo, y rescata el caso en que el primer confirm no llegó a notificar
            // (notificaciones apagadas entonces, destinatario inválido, etc.).
            await NotifyActiveAsync(enrollment, await ResolveCourseTitleAsync(enrollment.CourseId, cancellationToken), cancellationToken);
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
        await WriteEnrollmentAsync(active, cancellationToken);
        // Alimenta el timeline de aprendizaje al confirmar el pago → "enrolled".
        await AdvanceTrackingAsync(active.EnrollmentId, StageEnrolled, cancellationToken);
        // T4: avisarle al alumno que su matrícula quedó activa. Best-effort: un email
        // caído JAMÁS puede tumbar una matrícula ya pagada y persistida.
        await NotifyActiveAsync(active, await ResolveCourseTitleAsync(active.CourseId, cancellationToken), cancellationToken);
        return ToConfirmation(active);
    }

    public async Task<CourseProgress> GetProgressAsync(string courseId, string student, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(student))
        {
            return new CourseProgress(courseId ?? string.Empty, Array.Empty<string>(), 0);
        }

        var totalLessons = await ResolveTotalLessonsAsync(courseId, cancellationToken);
        return await BuildProgressAsync(courseId, student, totalLessons, cancellationToken);
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

        // Idempotente: el set de completadas es un HashSet OrdinalIgnoreCase —
        // marcar dos veces no duplica ni infla el %. Persiste como lista (round-trip
        // limpio con System.Text.Json) y se rehidrata al HashSet al leer.
        var storeKey = ProgressStoreKey(courseId, student);
        var existing = await LoadProgressAsync(storeKey, cancellationToken);
        var completed = existing is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(existing.Completed, StringComparer.OrdinalIgnoreCase);
        completed.Add(lessonId);
        await WriteProgressAsync(storeKey, new PersistedProgress(completed.ToList(), lessonId), cancellationToken);

        var progress = await BuildProgressAsync(courseId, student, allLessons.Count, cancellationToken);

        // Alimenta el timeline de aprendizaje según el avance (monotónico/idempotente
        // en el tracker): cualquier lección marcada → "in-progress"; el 100% →
        // "completed". Resuelve el enrollmentId del alumno para este curso (el mismo
        // ref que la matrícula sembró al activarse).
        var enrollmentId = await ResolveEnrollmentIdAsync(courseId, student, cancellationToken);
        if (enrollmentId is not null)
        {
            var stage = progress.Percent >= 100 ? "completed" : "in-progress";
            await AdvanceTrackingAsync(enrollmentId, stage, cancellationToken);
        }

        return progress;
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
        var progress = await BuildProgressAsync(courseId, student, totalLessons, cancellationToken);
        if (progress.Percent < 100)
        {
            return null; // El certificado solo se emite al 100%.
        }

        // Id estable derivado de (curso,alumno) → re-emitir devuelve el mismo
        // certificado (idempotente). El nombre del alumno sale del progreso si
        // hay matrícula; si no, del propio identificador.
        var certId = "cert-" + StableHash($"{courseId}|{student}");
        var studentName = await ResolveStudentNameAsync(student, cancellationToken);

        return new Certificate(
            Id: certId,
            CourseId: courseId,
            StudentName: studentName,
            IssuedAt: _now(),
            VerifyUrl: $"/academy/verify/{certId}");
    }

    // ── IEnrollmentMetrics (cara de lectura para el panel del instructor) ──

    public async Task<CourseEnrollmentStats> GetCourseStatsAsync(string courseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return new CourseEnrollmentStats(0, 0m);
        }

        var id = courseId.Trim();
        // Alumnos = matrículas activas del curso; ingreso = suma de sus totales
        // (los gratuitos suman 0). Cuenta sobre el store, donde cada matrícula
        // existe UNA sola vez (una entrada por enrollmentId) → sin doble conteo.
        var active = (await LoadAllEnrollmentsAsync(cancellationToken))
            .Where(e => e.Status == EnrollmentStatus.Active
                        && string.Equals(e.CourseId, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new CourseEnrollmentStats(active.Count, active.Sum(e => e.Total));
    }

    // ── Persistencia (deserialización defensiva) ───────────────────────

    private Task WriteEnrollmentAsync(PersistedEnrollment enrollment, CancellationToken cancellationToken)
        => _store.WriteAsync(
            EnrollmentResourceType,
            enrollment.EnrollmentId,
            JsonSerializer.Serialize(enrollment, _json),
            cancellationToken);

    private async Task<List<PersistedEnrollment>> LoadAllEnrollmentsAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(EnrollmentResourceType, cancellationToken);
        var enrollments = new List<PersistedEnrollment>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            PersistedEnrollment? enrollment;
            try { enrollment = JsonSerializer.Deserialize<PersistedEnrollment>(json, _json); }
            catch (JsonException) { continue; }   // archivo corrupto → se salta
            if (enrollment is not null) enrollments.Add(enrollment);
        }
        return enrollments;
    }

    // Lookup por orderRef: la matrícula se persiste UNA vez (keyed por enrollmentId,
    // que también existe en la rama gratis), así que el índice por orderRef se
    // resuelve filtrando. O(n) aceptable al volumen del motor y sin el riesgo de
    // desincronizar dos copias del mismo estado.
    private async Task<PersistedEnrollment?> LoadByOrderRefAsync(string? orderRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return null;
        }
        var all = await LoadAllEnrollmentsAsync(cancellationToken);
        return all.FirstOrDefault(e => string.Equals(e.OrderRef, orderRef, StringComparison.Ordinal));
    }

    private async Task<PersistedProgress?> LoadProgressAsync(string storeKey, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ProgressResourceType, storeKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<PersistedProgress>(json, _json); }
        catch (JsonException) { return null; }   // archivo corrupto → como si no existiera
    }

    private Task WriteProgressAsync(string storeKey, PersistedProgress progress, CancellationToken cancellationToken)
        => _store.WriteAsync(ProgressResourceType, storeKey, JsonSerializer.Serialize(progress, _json), cancellationToken);

    // ── Notificación del hecho "matrícula activa" (T4) ─────────────────

    // Emite el hecho por AMBAS ramas (gratis en EnrollAsync · paga en ConfirmAsync y su
    // short-circuit). El seam es opcional y la emisión best-effort (SafeDispatchAsync):
    // una matrícula ya persistida nunca se cae por un email.
    private Task NotifyActiveAsync(PersistedEnrollment enrollment, string? courseTitle, CancellationToken cancellationToken)
    {
        // Sin destinatario no se emite basura (el dispatcher ya filtra, pero no se le
        // inventa un placeholder). El motor valida email en EnrollAsync, así que esto
        // solo blinda contra una matrícula legacy/corrupta leída del store.
        if (string.IsNullOrWhiteSpace(enrollment.StudentEmail))
        {
            return Task.CompletedTask;
        }
        return NotificationEmission.SafeDispatchAsync(
            _notifier, BuildActiveNotification(enrollment, courseTitle), cancellationToken);
    }

    /// <summary>
    /// El hecho "matrícula activa" para T4. <b>DedupeKey explícito</b>: el
    /// <see cref="PersistedEnrollment.EnrollmentId"/> es un Guid fresco por llamada, así que
    /// la clave default ({Type}:{SubjectId}) NO identifica "este alumno en este curso" y el
    /// dedupe no serviría de nada. Consecuencia aceptada: si un alumno cancela y se
    /// re-matricula al mismo curso, no recibe un segundo aviso.
    /// </summary>
    private NotificationEvent BuildActiveNotification(PersistedEnrollment e, string? courseTitle)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(courseTitle))
        {
            data["Curso"] = courseTitle!;
        }

        return new NotificationEvent(
            Type: NotificationTypes.EnrollmentActive,
            SubjectId: e.EnrollmentId,
            ToEmail: e.StudentEmail,
            ToName: e.StudentName,
            Code: e.EnrollmentId,
            OccurredAt: _now(),
            // Rama gratis: Total = 0 → se omite el monto en vez de pintar "$ 0".
            Amount: e.Total > 0m ? e.Total : null,
            Currency: e.Total > 0m ? e.Currency : null,
            Data: data.Count > 0 ? data : null,
            ActionPath: $"/educacion/cursos/{e.CourseId}",
            DedupeKey: $"{NotificationTypes.EnrollmentActive}:{e.CourseId}:{e.StudentEmail.Trim().ToLowerInvariant()}");
    }

    private async Task<string?> ResolveCourseTitleAsync(string courseId, CancellationToken cancellationToken)
    {
        var detail = await _catalog.GetCourseAsync(courseId, cancellationToken);
        return detail?.Course.Title;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Avanza el timeline de aprendizaje de una matrícula (si hay tracker enchufado).
    // El tracker es idempotente/monotónico: re-avanzar a una etapa alcanzada es no-op.
    private async Task AdvanceTrackingAsync(string enrollmentId, string stage, CancellationToken cancellationToken)
    {
        if (_tracking is null || string.IsNullOrWhiteSpace(enrollmentId))
        {
            return;
        }
        await _tracking.AdvanceAsync(enrollmentId, stage, note: null, cancellationToken);
    }

    // Resuelve el enrollmentId de la matrícula activa de un alumno en un curso
    // (por email + courseId). Null si el alumno no está matriculado — el progreso
    // se registra igual, pero sin timeline hasta que exista la matrícula.
    private async Task<string?> ResolveEnrollmentIdAsync(string courseId, string student, CancellationToken cancellationToken)
    {
        var all = await LoadAllEnrollmentsAsync(cancellationToken);
        var match = all.FirstOrDefault(e =>
            string.Equals(e.CourseId, courseId, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(e.StudentEmail, student, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.StudentName, student, StringComparison.OrdinalIgnoreCase)));
        return match?.EnrollmentId;
    }

    private PersistedEnrollment CreateEnrollment(
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

    private async Task<CourseProgress> BuildProgressAsync(string courseId, string student, int totalLessons, CancellationToken cancellationToken)
    {
        var state = await LoadProgressAsync(ProgressStoreKey(courseId, student), cancellationToken);
        if (state is null || state.Completed.Count == 0)
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

    private async Task<string> ResolveStudentNameAsync(string student, CancellationToken cancellationToken)
    {
        // Si alguna matrícula del alumno (por email) tiene nombre, úsalo.
        var all = await LoadAllEnrollmentsAsync(cancellationToken);
        var match = all.FirstOrDefault(e =>
            string.Equals(e.StudentEmail, student, StringComparison.OrdinalIgnoreCase));
        return match?.StudentName ?? student;
    }

    private static EnrollmentConfirmation ToConfirmation(PersistedEnrollment e)
        => new(e.Status.ToString(), e.EnrollmentId, e.CourseId);

    // Clave lógica del progreso — IDÉNTICA a la que usaba el índice en memoria
    // ({alumno}|{curso}, case-insensitive por normalización a minúsculas).
    private static string ProgressKey(string courseId, string student)
        => $"{student.Trim().ToLowerInvariant()}|{courseId.Trim().ToLowerInvariant()}";

    // La clave lógica saneada para servir de key del store (que en el adapter
    // FileSystem es un nombre de archivo): determinista y estable entre reinicios.
    // El separador '|' no es válido como nombre de archivo → "__".
    private static string ProgressStoreKey(string courseId, string student)
    {
        var logical = ProgressKey(courseId, student).Replace("|", "__", StringComparison.Ordinal);
        var chars = logical.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var safe = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                       || c == '-' || c == '_' || c == '.' || c == '@';
            if (!safe) chars[i] = '-';
        }
        return new string(chars);
    }

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
}

/// <summary>
/// La forma SERIALIZADA de una matrícula (el antiguo <c>EnrollmentState</c> anidado
/// de <see cref="StubEnrollmentService"/>, promovido a top-level para round-trip
/// limpio con System.Text.Json — records posicionales deserializan por ctor).
/// Se persiste UNA sola vez por matrícula bajo la familia <c>"enrollments"</c>,
/// keyed por <see cref="EnrollmentId"/>: es el único identificador presente en
/// AMBAS ramas (<see cref="OrderRef"/> es null en la rama gratis).
/// </summary>
internal sealed record PersistedEnrollment(
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

/// <summary>
/// La forma SERIALIZADA del progreso de un alumno en un curso (el antiguo
/// <c>ProgressState</c> anidado). <see cref="Completed"/> se persiste como LISTA,
/// no como <c>HashSet</c>: System.Text.Json round-trippea la colección pero PERDERÍA
/// el comparador <c>OrdinalIgnoreCase</c> del set original — el motor rehidrata el
/// HashSet con su comparador al leer, preservando la idempotencia de MarkLessonAsync.
/// </summary>
internal sealed record PersistedProgress(
    IReadOnlyList<string> Completed,
    string? LastLessonId);
