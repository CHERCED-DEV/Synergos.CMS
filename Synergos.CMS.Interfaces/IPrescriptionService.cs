namespace Synergos.CMS.Interfaces;

/// <summary>
/// Registro de recetas médicas (ADR 0098). <strong>RECORD-KEEPER</strong>:
/// registra la receta que el doctor emite — NO valida clínicamente (sin chequeo
/// de interacciones, contraindicaciones ni dosis razonable). Esa responsabilidad
/// es del profesional. Append-only: revocar marca estado, no borra.
/// </summary>
/// <remarks>
/// Implementación por defecto: <c>Synergos.CMS.Web.Services.FileSystemPrescriptionService</c>
/// sobre <see cref="IPhiStore"/>. La autorización (rol doctor) la aplica
/// <see cref="IPhiAccessGuard"/> en el endpoint, NO acá.
/// </remarks>
public interface IPrescriptionService
{
    /// <summary>Registra una receta (estado <c>active</c>). NO valida clínicamente.</summary>
    Task<PrescriptionRecord> IssueAsync(IssuePrescriptionRequest request, CancellationToken cancellationToken);

    /// <summary>Devuelve una receta por id, o null si no existe.</summary>
    Task<PrescriptionRecord?> GetAsync(Guid prescriptionId, CancellationToken cancellationToken);

    /// <summary>Recetas de un paciente, ordenadas por emisión desc.</summary>
    Task<IReadOnlyList<PrescriptionRecord>> ListForPatientAsync(Guid patientKey, CancellationToken cancellationToken);

    /// <summary>Marca una receta como <c>revoked</c>. False si no existe.</summary>
    Task<bool> RevokeAsync(Guid prescriptionId, CancellationToken cancellationToken);
}

/// <summary>Solicitud de emisión de receta. Tiempos en UTC.</summary>
public sealed record IssuePrescriptionRequest(
    Guid PatientKey,
    Guid IssuedByDoctorKey,
    string MedicationName,
    string Dosage,
    string Frequency,
    string? Instructions,
    DateTime ValidFromUtc,
    DateTime ValidToUtc);

/// <summary>Receta registrada. <paramref name="Status"/>: active | expired | revoked.</summary>
public sealed record PrescriptionRecord(
    Guid PrescriptionId,
    Guid PatientKey,
    Guid IssuedByDoctorKey,
    string MedicationName,
    string Dosage,
    string Frequency,
    string? Instructions,
    DateTime ValidFromUtc,
    DateTime ValidToUtc,
    DateTime IssuedAtUtc,
    string Status);
