namespace Synergos.CMS.Interfaces;

/// <summary>
/// Repositorio de historia clínica de pacientes (ADR 0098). Append-only:
/// cada actualización crea una versión nueva y archiva la anterior (rastro
/// inmutable). <c>PatientKey</c> es una clave clínica DISTINTA de
/// <c>MemberKey</c> — el RTBF de un Member no deja PHI huérfana.
/// </summary>
/// <remarks>
/// Implementación por defecto: <c>Synergos.CMS.Web.Services.FileSystemPatientRepository</c>
/// sobre <see cref="IPhiStore"/> (cifrado, atómico). El acceso se valida SIEMPRE
/// con <see cref="IPhiAccessGuard"/> en el endpoint, NO acá.
/// </remarks>
public interface IPatientRepository
{
    /// <summary>Versión actual de un paciente, o null si no existe.</summary>
    Task<PatientRecord?> GetAsync(Guid patientKey, CancellationToken cancellationToken);

    /// <summary>
    /// Crea (si <see cref="PatientRecord.PatientKey"/> es vacío) o actualiza un
    /// paciente. Devuelve la clave. Incrementa la versión y archiva la previa.
    /// </summary>
    Task<Guid> UpsertAsync(PatientRecord record, CancellationToken cancellationToken);

    /// <summary>Resúmenes (sin PHI clínica) de los pacientes vigentes, filtrables.</summary>
    Task<IReadOnlyList<PatientSummary>> ListAsync(PatientQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Registro clínico inmutable de un paciente. Los campos clínicos
/// (<paramref name="ChiefComplaint"/>, <paramref name="FindingsNotes"/>,
/// <paramref name="AssessmentNotes"/>) son PHI.
/// </summary>
/// <param name="PatientKey">Clave clínica estable (≠ MemberKey).</param>
/// <param name="MemberKey">Member dueño (para auto-acceso del paciente). Se
///   de-identifica antes del RTBF.</param>
/// <param name="DisplayName">Nombre del paciente.</param>
/// <param name="DateOfBirth">Fecha de nacimiento.</param>
/// <param name="ChiefComplaint">Motivo de consulta.</param>
/// <param name="FindingsNotes">Hallazgos.</param>
/// <param name="AssessmentNotes">Valoración.</param>
/// <param name="CreatedAtUtc">Creación del paciente (se preserva entre versiones).</param>
/// <param name="CreatedByDoctorKey">Doctor que creó/edita.</param>
/// <param name="VersionNumber">Versión (incremental).</param>
/// <param name="IsDeleted">Borrado lógico (oculto de los listados).</param>
public sealed record PatientRecord(
    Guid PatientKey,
    Guid MemberKey,
    string DisplayName,
    DateTime DateOfBirth,
    string? ChiefComplaint,
    string? FindingsNotes,
    string? AssessmentNotes,
    DateTime CreatedAtUtc,
    Guid CreatedByDoctorKey,
    int VersionNumber,
    bool IsDeleted);

/// <summary>Resumen seguro para listados — sin diagnósticos ni notas clínicas.</summary>
public sealed record PatientSummary(
    Guid PatientKey,
    string DisplayName,
    int AgeYears,
    DateTime LastVisitUtc,
    int TotalVisits);

/// <summary>Filtro de listado de pacientes.</summary>
public sealed record PatientQuery(Guid? DoctorKey = null, int Limit = 200);
