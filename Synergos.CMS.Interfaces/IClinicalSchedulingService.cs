namespace Synergos.CMS.Interfaces;

/// <summary>
/// Agenda de citas del dashboard EHR-lite (OLA 5): reservar una cita médico+slot
/// y listar las citas de un día. Capa de DEMO aditiva que alimenta la app Angular
/// <c>module-healthcare-ehr</c> vía <c>/api/ehr</c>.
/// </summary>
/// <remarks>
/// <strong>Reusa el motor de reservas (spec §4):</strong> la cita es un RECURSO
/// RESERVABLE POLIMÓRFICO, igual que habitación/asiento. <c>BookAsync</c> aparta el
/// slot con <see cref="IReservationService.HoldItemAsync"/> (hold-timeout incluido) y,
/// como en demo no hay copago obligatorio, lo confirma de inmediato con
/// <see cref="IReservationService.ConfirmAsync"/> — la misma máquina de estados
/// Held→Confirmed de hoteles/aerolíneas. El copago opcional engancharía
/// <see cref="IPaymentProvider"/> entre Hold y Confirm sin tocar este contrato.
///
/// Distinto del <see cref="IAppointmentScheduler"/> de producción (ADR 0098, agenda
/// PHI con anti-overbooking sobre el PHI store): este es el seam de DEMO sobre datos
/// sembrados. Tipo prefijado <c>ClinicalAppointment</c> para no colisionar con
/// <see cref="AppointmentSlot"/>. Stub-first (ADR 0002).
/// </remarks>
public interface IClinicalSchedulingService
{
    /// <summary>
    /// Reserva una cita para un paciente con un médico en un slot. Aparta el slot
    /// con el motor de reservas y la confirma (demo sin copago obligatorio).
    /// Devuelve la cita confirmada. Lanza si paciente/médico son inválidos o el
    /// slot ya está ocupado.
    /// </summary>
    Task<ClinicalAppointment> BookAsync(BookAppointmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista las citas de una fecha (zona del consultorio), opcionalmente filtradas
    /// por médico, ordenadas por hora ascendente. Vacío si no hay.
    /// </summary>
    Task<IReadOnlyList<ClinicalAppointment>> GetByDateAsync(DateOnly date, string? doctorId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cita registrada del EHR-lite. <see cref="ReservationId"/> liga la cita con la
/// reserva del motor (<see cref="IReservationService"/>). <c>Status</c>:
/// booked | checked-in | in-progress | done | no-show | cancelled.
/// </summary>
public sealed record ClinicalAppointment(
    string Id,
    string PatientId,
    string PatientName,
    string DoctorId,
    string DoctorName,
    string Specialty,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    string ReservationId);

/// <summary>
/// Solicitud de reserva de cita. <paramref name="Slot"/> es el inicio del slot
/// (UTC) elegido por el paciente en el slot-picker.
/// </summary>
public sealed record BookAppointmentRequest(
    string PatientId,
    string DoctorId,
    DateTime Slot);
