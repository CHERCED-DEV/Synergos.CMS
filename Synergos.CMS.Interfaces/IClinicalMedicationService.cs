namespace Synergos.CMS.Interfaces;

/// <summary>
/// Medicamentos activos + solicitudes de resurtido (refill) del dashboard EHR-lite
/// (OLA 7, portal paciente). Lista la medicación activa del paciente y captura la
/// solicitud de refill que cae al In Basket del médico. Capa de DEMO aditiva sobre
/// datos sembrados que alimenta la app Angular <c>module-mychart</c> vía
/// <c>/api/ehr/medications</c> + <c>/api/ehr/refill</c>.
/// </summary>
/// <remarks>
/// La medicación activa se DERIVA de las recetas vivas (<see cref="IClinicalPrescriptionService"/>)
/// — no hay store paralelo (DIP, misma técnica que los seams que componen otro seam).
/// El refill NO escribe la medicación: crea una solicitud que el controller enruta al
/// In Basket del médico vía <see cref="IMessagingService"/> (contexto 'clinical') y
/// audita vía <see cref="IAuditTrailWriter"/> (ADR 0037). Stub-first (ADR 0002): el
/// default <c>StubClinicalMedicationService</c> compone el seam de recetas + mantiene
/// las solicitudes en memoria; un adapter real (pharmacy / eRx) reemplaza el seam sin
/// tocar el controller. Tipos prefijados <c>Ehr*</c> para no colisionar.
/// </remarks>
public interface IClinicalMedicationService
{
    /// <summary>
    /// Devuelve la medicación activa del paciente (derivada de sus recetas vivas),
    /// de la más reciente a la más antigua. Vacío si no hay. Audita la lectura como
    /// acceso a PHI.
    /// </summary>
    Task<IReadOnlyList<EhrMedication>> GetActiveForPatientAsync(string patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra una solicitud de resurtido para una medicación del paciente (estado
    /// <c>pending</c>). Devuelve la solicitud creada. Audita la escritura como acceso
    /// a PHI. Lanza si el paciente o la medicación son inválidos.
    /// </summary>
    Task<EhrRefillRequest> RequestRefillAsync(RefillRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Una medicación activa del paciente (proyección de una línea de receta viva).
/// <see cref="MedicationId"/> es estable para solicitar su refill.
/// </summary>
public sealed record EhrMedication(
    string MedicationId,
    string PatientId,
    string MedicationName,
    string Dosage,
    string Frequency,
    string? Instructions,
    string PrescribedByDoctorId,
    string PrescribedByDoctorName,
    DateTime PrescribedAtUtc,
    string Status,
    int? RefillsRemaining);

/// <summary>
/// Solicitud de resurtido registrada. <see cref="Status"/>:
/// pending | approved | denied. Cae al In Basket del médico prescriptor.
/// </summary>
public sealed record EhrRefillRequest(
    string Id,
    string PatientId,
    string MedicationId,
    string MedicationName,
    string ProviderId,
    DateTime RequestedAtUtc,
    string Status,
    string? Note);

/// <summary>Solicitud de alta de un refill (la manda el paciente desde el portal).</summary>
public sealed record RefillRequest(
    string PatientId,
    string MedicationId,
    string? Note = null);
