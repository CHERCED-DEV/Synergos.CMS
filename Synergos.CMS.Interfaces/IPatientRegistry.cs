namespace Synergos.CMS.Interfaces;

/// <summary>
/// Registro de pacientes del dashboard clínico <strong>EHR-lite</strong> (OLA 5,
/// demo seria del aplicativo). Búsqueda + ficha demográfica. Capa de DEMO
/// aditiva sobre datos sembrados — distinta del repositorio PHI cifrado y
/// fail-closed de producción (<see cref="IPatientRepository"/>, ADR 0098): este
/// seam alimenta la app Angular <c>module-healthcare-ehr</c> vía <c>/api/ehr</c>.
/// </summary>
/// <remarks>
/// Stub-first (igual que <see cref="IReservationService"/>/<see cref="IPaymentProvider"/>):
/// el default <c>StubPatientRegistry</c> (Application, lógica pura, ADR 0002) sirve un
/// padrón sembrado en memoria; un adapter real (DB / HIS) se enchufa sin tocar el
/// controller. Tipos prefijados <c>Ehr*</c> para no colisionar con los records de
/// <see cref="IPatientRepository"/> en el mismo namespace.
/// </remarks>
public interface IPatientRegistry
{
    /// <summary>
    /// Busca pacientes por texto libre (nombre/documento/email). Texto vacío
    /// devuelve el padrón completo ordenado por nombre. Nunca lanza.
    /// </summary>
    Task<IReadOnlyList<EhrPatient>> SearchAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>Devuelve un paciente por id, o null si no existe.</summary>
    Task<EhrPatient?> GetAsync(string patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Paciente del EHR-lite con su ficha demográfica. Datos de DEMO (sembrados);
/// no es PHI de producción.
/// </summary>
public sealed record EhrPatient(
    string Id,
    string FullName,
    string DocumentId,
    string Gender,
    DateOnly DateOfBirth,
    int AgeYears,
    string Phone,
    string Email,
    string City,
    string BloodType,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<string> ChronicConditions,
    string? PrimaryDoctorId,
    string? AvatarUrl);
