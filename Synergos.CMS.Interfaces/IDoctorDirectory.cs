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
/// Un profesional del directorio, con su especialidad y su disponibilidad de agenda.
/// </summary>
/// <remarks>
/// <b>El <see cref="Id"/> es el SUJETO, y no el recurso.</b> Es lo que viaja como
/// <c>professionalId</c> hacia <c>Bff.Salud</c>; el identificador del recurso de
/// <c>Api.Booking</c> lo genera la capacidad y se resuelve con
/// <c>GET /v1/resources?subjectKind=&amp;subjectId=</c> (HU #25). Ninguna convención de este
/// lado puede acertarlo, y ya costó una vuelta entera intentarlo.
/// </remarks>
/// <param name="WorkingDays">Días laborables (DayOfWeek) en que atiende.</param>
/// <param name="SlotStartHour">Hora local de inicio de atención (0-23).</param>
/// <param name="SlotEndHour">Hora local de fin de atención (0-23).</param>
/// <param name="SlotMinutes">Duración de cada slot, en minutos.</param>
/// <param name="Phone">Teléfono del consultorio, o vacío si no consta.</param>
/// <param name="Email">Correo del consultorio, o vacío si no consta.</param>
/// <param name="AcceptingPatients">
/// Si admite pacientes nuevos. <b><c>null</c> es «no consta», y no «no»</b> (#111): cerrarle
/// la lista a quien sí recibe es tan falso como abrírsela a quien no. El directorio sembrado
/// no lo sabe y lo deja en <c>null</c>; el autorado lo dice sólo cuando el editor lo eligió.
/// </param>
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
    int SlotMinutes,
    string Phone = "",
    string Email = "",
    bool? AcceptingPatients = null);
