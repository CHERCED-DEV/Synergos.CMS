namespace Synergos.CMS.Interfaces;

/// <summary>
/// Directorio de médicos/staff del dashboard clínico EHR-lite (OLA 5). Alimenta
/// el slot-picker de la agenda y el booking de citas. Capa de DEMO aditiva sobre
/// datos sembrados — no es el roster de Members de producción.
/// </summary>
/// <remarks>
/// Stub-first (ADR 0002): el default <c>StubDoctorDirectory</c> sirve un staff
/// sembrado en memoria; un adapter real (DB / HR) se enchufa sin tocar el
/// controller. Tipo prefijado <c>MedicalDoctor</c> para no colisionar.
/// </remarks>
public interface IDoctorDirectory
{
    /// <summary>Lista los médicos, opcionalmente filtrados por especialidad. Nunca lanza.</summary>
    Task<IReadOnlyList<MedicalDoctor>> ListAsync(string? specialty = null, CancellationToken cancellationToken = default);

    /// <summary>Devuelve un médico por id, o null si no existe.</summary>
    Task<MedicalDoctor?> GetAsync(string doctorId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Médico del EHR-lite con especialidad y disponibilidad de agenda. Datos de DEMO.
/// </summary>
/// <param name="WorkingDays">Días laborables (DayOfWeek) en que atiende.</param>
/// <param name="SlotStartHour">Hora local de inicio de atención (0-23).</param>
/// <param name="SlotEndHour">Hora local de fin de atención (0-23).</param>
/// <param name="SlotMinutes">Duración de cada slot, en minutos.</param>
public sealed record MedicalDoctor(
    string Id,
    string FullName,
    string Specialty,
    string LicenseNumber,
    double Rating,
    int YearsExperience,
    string? AvatarUrl,
    IReadOnlyList<DayOfWeek> WorkingDays,
    int SlotStartHour,
    int SlotEndHour,
    int SlotMinutes);
