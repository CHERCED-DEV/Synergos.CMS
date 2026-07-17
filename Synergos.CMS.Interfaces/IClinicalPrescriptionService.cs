namespace Synergos.CMS.Interfaces;

/// <summary>
/// Recetas del dashboard EHR-lite (OLA 5): emitir una receta (uno o más ítems de
/// medicación) + listar por paciente. Capa de DEMO aditiva sobre datos sembrados
/// que alimenta la app Angular <c>module-healthcare-ehr</c> vía <c>/api/ehr</c>.
/// </summary>
/// <remarks>
/// <strong>RECORD-KEEPER</strong> (igual que <see cref="IPrescriptionService"/> de
/// producción): registra lo que el clínico prescribe — NO valida interacciones ni
/// dosis. Cada lectura/escritura audita acceso a PHI vía <see cref="IAuditTrailWriter"/>.
/// Stub-first (ADR 0002). Tipos prefijados <c>Ehr*</c> para no colisionar con
/// <see cref="PrescriptionRecord"/>/<see cref="IssuePrescriptionRequest"/> del seam
/// de producción en el mismo namespace.
/// </remarks>
public interface IClinicalPrescriptionService
{
    /// <summary>
    /// Registra una receta nueva (estado <c>active</c>) con sus ítems. Devuelve la
    /// receta creada. Audita la escritura como acceso a PHI. Lanza si no hay ítems.
    /// </summary>
    Task<EhrPrescription> AddAsync(AddPrescriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recetas de un paciente, de la más reciente a la más antigua. Vacío si no hay.
    /// Audita la lectura como acceso a PHI.
    /// </summary>
    Task<IReadOnlyList<EhrPrescription>> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default);
}

/// <summary>Un ítem de medicación dentro de una receta.</summary>
public sealed record EhrPrescriptionItem(
    string MedicationName,
    string Dosage,
    string Frequency,
    int DurationDays,
    string? Instructions = null);

/// <summary>Receta registrada del EHR-lite. <c>Status</c>: active | expired | revoked.</summary>
public sealed record EhrPrescription(
    string Id,
    string PatientId,
    string DoctorId,
    string DoctorName,
    DateTime IssuedAtUtc,
    IReadOnlyList<EhrPrescriptionItem> Items,
    string Status,
    string? EncounterId = null);

/// <summary>Solicitud de alta de una receta.</summary>
public sealed record AddPrescriptionRequest(
    string PatientId,
    string DoctorId,
    IReadOnlyList<EhrPrescriptionItem> Items,
    string? EncounterId = null);
