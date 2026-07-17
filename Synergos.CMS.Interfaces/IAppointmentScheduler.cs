namespace Synergos.CMS.Interfaces;

/// <summary>
/// Agenda de citas clínicas (ADR 0098). Reserva, cancela y lista citas con
/// protección anti-overbooking. La decisión de conflicto es lógica PURA
/// (Application); el lock + persistencia atómica viven en la implementación Web.
/// </summary>
/// <remarks>
/// Implementación por defecto: <c>Synergos.CMS.Web.Services.LockingAppointmentScheduler</c>
/// sobre <see cref="IPhiStore"/>, con lock por-doctor. El acceso se valida con
/// <see cref="IPhiAccessGuard"/> en el endpoint, NO acá. Single-instance (D1):
/// el lock es in-process; multi-instancia requeriría lock distribuido.
/// </remarks>
public interface IAppointmentScheduler
{
    /// <summary>
    /// Reserva una cita si no hay conflicto con las citas vigentes del doctor
    /// (más allá de la tolerancia de overbooking configurada). Idempotente por
    /// solapamiento exacto del mismo paciente/doctor/horario.
    /// </summary>
    Task<AppointmentBookResult> BookAsync(AppointmentRequest request, CancellationToken cancellationToken);

    /// <summary>Cancela una cita (marca cancelled, no la borra). False si no existe.</summary>
    Task<bool> CancelAsync(Guid appointmentId, string reason, CancellationToken cancellationToken);

    /// <summary>Lista citas según el filtro, ordenadas por inicio ascendente.</summary>
    Task<IReadOnlyList<AppointmentSlot>> ListAsync(AppointmentQuery query, CancellationToken cancellationToken);
}

/// <summary>Solicitud de reserva de cita. Tiempos en UTC.</summary>
public sealed record AppointmentRequest(
    Guid PatientKey,
    Guid DoctorKey,
    DateTime StartUtc,
    DateTime EndUtc,
    Guid CreatedByKey);

/// <summary>Resultado de una reserva: la cita creada, o el motivo del conflicto.</summary>
public sealed record AppointmentBookResult(bool Booked, AppointmentSlot? Slot, string? ConflictReason);

/// <summary>Cita registrada. Tiempos en UTC; la UI convierte a la zona del consultorio.
/// <c>Status</c>: <c>"booked"</c> | <c>"cancelled"</c> | <c>"completed"</c>.</summary>
public sealed record AppointmentSlot(
    Guid AppointmentId,
    Guid PatientKey,
    Guid DoctorKey,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    Guid CreatedByKey);

/// <summary>Filtro de listado de citas. Todos opcionales.</summary>
public sealed record AppointmentQuery(
    Guid? DoctorKey = null,
    Guid? PatientKey = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    bool IncludeCancelled = false,
    int Limit = 500);
