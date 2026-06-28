using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del dashboard clínico <strong>EHR-lite</strong> (OLA 5 — dominio
/// Healthcare, doc healthcare-app-spec). La consume la app Angular
/// <c>module-healthcare-ehr</c>. Es la capa de DEMO del aplicativo (entrar = caer
/// directo en un panel admin clínico real con datos sembrados coherentes), DISTINTA
/// del núcleo PHI de producción fail-closed de <see cref="HealthcareApiController"/>
/// (<c>/api/healthcare</c>, ADR 0098).
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en
/// los seams (Application, sin Umbraco — ADR 0002):
/// <list type="bullet">
/// <item><see cref="IPatientRegistry"/> / <see cref="IDoctorDirectory"/> — padrón + staff.</item>
/// <item><see cref="IClinicalRecordService"/> — historia + encuentros (SOAP); cada
///   acceso a PHI se audita vía <see cref="IAuditTrailWriter"/> dentro del seam.</item>
/// <item><see cref="IClinicalPrescriptionService"/> — recetas por paciente.</item>
/// <item><see cref="IClinicalSchedulingService"/> — agenda, reusa el motor de reservas
///   (<see cref="IReservationService"/>) + pago (<see cref="IPaymentProvider"/>).</item>
/// </list>
/// Contrato (lo programa el agente UI):
/// <c>GET patients?q · GET patient/{id} · GET doctors · GET appointments?date ·
/// POST appointment · POST encounter · POST prescription</c>.
/// </remarks>
[ApiController]
[Route("api/ehr")]
public sealed class EhrController : ControllerBase
{
    private readonly IPatientRegistry _patients;
    private readonly IDoctorDirectory _doctors;
    private readonly IClinicalRecordService _records;
    private readonly IClinicalPrescriptionService _prescriptions;
    private readonly IClinicalSchedulingService _scheduling;

    public EhrController(
        IPatientRegistry patients,
        IDoctorDirectory doctors,
        IClinicalRecordService records,
        IClinicalPrescriptionService prescriptions,
        IClinicalSchedulingService scheduling)
    {
        _patients = patients;
        _doctors = doctors;
        _records = records;
        _prescriptions = prescriptions;
        _scheduling = scheduling;
    }

    // ── 1. Pacientes (lista buscable) ──────────────────────────────────
    // GET /api/ehr/patients?q= → { patients:[...] }
    [HttpGet("patients")]
    public async Task<IActionResult> Patients([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var patients = await _patients.SearchAsync(q, cancellationToken);
        return Ok(new PatientsResponse(patients.Select(ToPatientDto).ToList()));
    }

    // ── 2. Patient chart ───────────────────────────────────────────────
    // GET /api/ehr/patient/{id} → { patient, history, encounters, prescriptions, appointments }
    [HttpGet("patient/{id}")]
    public async Task<IActionResult> Patient(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del paciente es requerido." });
        }

        var patient = await _patients.GetAsync(id, cancellationToken);
        if (patient is null)
        {
            return NotFound(new { error = $"Paciente '{id}' no encontrado." });
        }

        var history = await _records.GetHistoryAsync(id, cancellationToken);
        var encounters = await _records.GetEncountersAsync(id, cancellationToken);
        var prescriptions = await _prescriptions.GetForPatientAsync(id, cancellationToken);
        // Citas del paciente: filtra de la agenda viva (sin fecha → todas).
        var appointments = await CollectPatientAppointmentsAsync(id, cancellationToken);

        return Ok(new PatientChartResponse(
            Patient: ToPatientDto(patient),
            History: history is null ? null : ToHistoryDto(history),
            Encounters: encounters.Select(ToEncounterDto).ToList(),
            Prescriptions: prescriptions.Select(ToPrescriptionDto).ToList(),
            Appointments: appointments.Select(ToAppointmentDto).ToList()));
    }

    // ── 3. Doctores ────────────────────────────────────────────────────
    // GET /api/ehr/doctors?specialty= → { doctors:[...] }
    [HttpGet("doctors")]
    public async Task<IActionResult> Doctors([FromQuery] string? specialty, CancellationToken cancellationToken)
    {
        var doctors = await _doctors.ListAsync(specialty, cancellationToken);
        return Ok(new DoctorsResponse(doctors.Select(ToDoctorDto).ToList()));
    }

    // ── 4. Citas por fecha ─────────────────────────────────────────────
    // GET /api/ehr/appointments?date=YYYY-MM-DD&doctorId= → { appointments:[...] }
    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] string? date, [FromQuery] string? doctorId, CancellationToken cancellationToken)
    {
        // Sin fecha → hoy (UTC), para que el dashboard caiga con el schedule del día.
        var day = ParseDateOrToday(date);
        var appointments = await _scheduling.GetByDateAsync(day, doctorId, cancellationToken);
        return Ok(new AppointmentsResponse(appointments.Select(ToAppointmentDto).ToList()));
    }

    // ── 5. Reservar cita ───────────────────────────────────────────────
    // POST /api/ehr/appointment { patientId, doctorId, slot } → { appointment }
    [HttpPost("appointment")]
    public async Task<IActionResult> BookAppointment([FromBody] BookAppointmentBody? body, CancellationToken cancellationToken)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.PatientId)
            || string.IsNullOrWhiteSpace(body.DoctorId))
        {
            return BadRequest(new { error = "patientId, doctorId y slot son requeridos." });
        }
        if (body.Slot is not { } slot)
        {
            return BadRequest(new { error = "slot (fecha/hora UTC) es requerido y debe ser válido." });
        }

        try
        {
            var appointment = await _scheduling.BookAsync(
                new BookAppointmentRequest(body.PatientId.Trim(), body.DoctorId.Trim(), slot),
                cancellationToken);
            return Ok(new AppointmentEnvelope(ToAppointmentDto(appointment)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Slot ocupado / conflicto.
            return Conflict(new { error = ex.Message });
        }
    }

    // ── 6. Registrar encuentro (SOAP) ──────────────────────────────────
    // POST /api/ehr/encounter { patientId, soap, doctorId?, reasonForVisit?, diagnosisCode? } → { encounter }
    [HttpPost("encounter")]
    public async Task<IActionResult> AddEncounter([FromBody] AddEncounterBody? body, CancellationToken cancellationToken)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.PatientId) || body.Soap is null)
        {
            return BadRequest(new { error = "patientId y soap son requeridos." });
        }

        try
        {
            var encounter = await _records.AddEncounterAsync(
                new AddEncounterRequest(
                    PatientId: body.PatientId.Trim(),
                    DoctorId: (body.DoctorId ?? string.Empty).Trim(),
                    ReasonForVisit: body.ReasonForVisit ?? string.Empty,
                    Soap: new SoapNote(
                        Subjective: body.Soap.Subjective ?? string.Empty,
                        Objective: body.Soap.Objective ?? string.Empty,
                        Assessment: body.Soap.Assessment ?? string.Empty,
                        Plan: body.Soap.Plan ?? string.Empty,
                        Vitals: body.Soap.Vitals is null
                            ? null
                            : new ClinicalVitals(
                                body.Soap.Vitals.SystolicMmHg, body.Soap.Vitals.DiastolicMmHg,
                                body.Soap.Vitals.HeartRateBpm, body.Soap.Vitals.TemperatureC,
                                body.Soap.Vitals.WeightKg, body.Soap.Vitals.HeightCm,
                                body.Soap.Vitals.GlucoseMgDl, body.Soap.Vitals.OxygenSaturationPct)),
                    DiagnosisCode: body.DiagnosisCode),
                cancellationToken);
            return Ok(new EncounterEnvelope(ToEncounterDto(encounter)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── 7. Registrar receta ────────────────────────────────────────────
    // POST /api/ehr/prescription { patientId, items:[...], doctorId?, encounterId? } → { prescription }
    [HttpPost("prescription")]
    public async Task<IActionResult> AddPrescription([FromBody] AddPrescriptionBody? body, CancellationToken cancellationToken)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.PatientId) || body.Items is null || body.Items.Count == 0)
        {
            return BadRequest(new { error = "patientId e items (al menos uno) son requeridos." });
        }

        try
        {
            var prescription = await _prescriptions.AddAsync(
                new AddPrescriptionRequest(
                    PatientId: body.PatientId.Trim(),
                    DoctorId: (body.DoctorId ?? string.Empty).Trim(),
                    Items: body.Items.Select(i => new EhrPrescriptionItem(
                        MedicationName: i.MedicationName ?? string.Empty,
                        Dosage: i.Dosage ?? string.Empty,
                        Frequency: i.Frequency ?? string.Empty,
                        DurationDays: i.DurationDays,
                        Instructions: i.Instructions)).ToList(),
                    EncounterId: body.EncounterId),
                cancellationToken);
            return Ok(new PrescriptionEnvelope(ToPrescriptionDto(prescription)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ClinicalAppointment>> CollectPatientAppointmentsAsync(string patientId, CancellationToken cancellationToken)
    {
        // La agenda se consulta por fecha; barre una ventana razonable alrededor de
        // hoy para reunir las citas del paciente sin un seam de "por paciente"
        // (mantiene ISP en el seam de agenda). Suficiente para la demo.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<ClinicalAppointment>();
        for (var offset = -30; offset <= 60; offset++)
        {
            var day = today.AddDays(offset);
            var dayAppts = await _scheduling.GetByDateAsync(day, doctorId: null, cancellationToken);
            result.AddRange(dayAppts.Where(a => string.Equals(a.PatientId, patientId, StringComparison.Ordinal)));
        }
        return result.OrderBy(a => a.StartUtc).ToList();
    }

    private static DateOnly ParseDateOrToday(string? date)
        => DateOnly.TryParse(date, out var parsed) ? parsed : DateOnly.FromDateTime(DateTime.UtcNow);

    private static PatientDto ToPatientDto(EhrPatient p) => new(
        Id: p.Id, FullName: p.FullName, DocumentId: p.DocumentId, Gender: p.Gender,
        DateOfBirth: p.DateOfBirth.ToString("yyyy-MM-dd"), AgeYears: p.AgeYears,
        Phone: p.Phone, Email: p.Email, City: p.City, BloodType: p.BloodType,
        Allergies: p.Allergies, ChronicConditions: p.ChronicConditions,
        PrimaryDoctorId: p.PrimaryDoctorId, AvatarUrl: p.AvatarUrl);

    private static DoctorDto ToDoctorDto(MedicalDoctor d) => new(
        Id: d.Id, FullName: d.FullName, Specialty: d.Specialty, LicenseNumber: d.LicenseNumber,
        Rating: d.Rating, YearsExperience: d.YearsExperience, AvatarUrl: d.AvatarUrl,
        WorkingDays: d.WorkingDays.Select(w => (int)w).ToList(),
        SlotStartHour: d.SlotStartHour, SlotEndHour: d.SlotEndHour, SlotMinutes: d.SlotMinutes);

    private static HistoryDto ToHistoryDto(ClinicalHistory h) => new(
        PatientId: h.PatientId, ChiefComplaint: h.ChiefComplaint,
        ActiveProblems: h.ActiveProblems, PastMedicalHistory: h.PastMedicalHistory,
        Allergies: h.Allergies, CurrentMedications: h.CurrentMedications,
        BaselineVitals: h.BaselineVitals is null ? null : ToVitalsDto(h.BaselineVitals),
        LastVisitUtc: h.LastVisitUtc, TotalEncounters: h.TotalEncounters);

    private static EncounterDto ToEncounterDto(ClinicalEncounter e) => new(
        Id: e.Id, PatientId: e.PatientId, DoctorId: e.DoctorId, DoctorName: e.DoctorName,
        OccurredAtUtc: e.OccurredAtUtc, ReasonForVisit: e.ReasonForVisit,
        Soap: new SoapDto(e.Soap.Subjective, e.Soap.Objective, e.Soap.Assessment, e.Soap.Plan,
            e.Soap.Vitals is null ? null : ToVitalsDto(e.Soap.Vitals)),
        DiagnosisCode: e.DiagnosisCode, SignedByClinician: e.SignedByClinician);

    private static PrescriptionDto ToPrescriptionDto(EhrPrescription p) => new(
        Id: p.Id, PatientId: p.PatientId, DoctorId: p.DoctorId, DoctorName: p.DoctorName,
        IssuedAtUtc: p.IssuedAtUtc, Status: p.Status, EncounterId: p.EncounterId,
        Items: p.Items.Select(i => new PrescriptionItemDto(
            i.MedicationName, i.Dosage, i.Frequency, i.DurationDays, i.Instructions)).ToList());

    private static AppointmentDto ToAppointmentDto(ClinicalAppointment a) => new(
        Id: a.Id, PatientId: a.PatientId, PatientName: a.PatientName,
        DoctorId: a.DoctorId, DoctorName: a.DoctorName, Specialty: a.Specialty,
        StartUtc: a.StartUtc, EndUtc: a.EndUtc, Status: a.Status, ReservationId: a.ReservationId);

    private static VitalsDto ToVitalsDto(ClinicalVitals v) => new(
        v.SystolicMmHg, v.DiastolicMmHg, v.HeartRateBpm, v.TemperatureC,
        v.WeightKg, v.HeightCm, v.GlucoseMgDl, v.OxygenSaturationPct);

    // ── Request bodies (binding del módulo UI) ─────────────────────────

    public sealed record BookAppointmentBody(string PatientId, string DoctorId, DateTime? Slot);

    public sealed record VitalsBody(
        double? SystolicMmHg, double? DiastolicMmHg, double? HeartRateBpm, double? TemperatureC,
        double? WeightKg, double? HeightCm, double? GlucoseMgDl, double? OxygenSaturationPct);

    public sealed record SoapBody(string? Subjective, string? Objective, string? Assessment, string? Plan, VitalsBody? Vitals);

    public sealed record AddEncounterBody(string PatientId, SoapBody Soap, string? DoctorId, string? ReasonForVisit, string? DiagnosisCode);

    public sealed record PrescriptionItemBody(string? MedicationName, string? Dosage, string? Frequency, int DurationDays, string? Instructions);

    public sealed record AddPrescriptionBody(string PatientId, IReadOnlyList<PrescriptionItemBody> Items, string? DoctorId, string? EncounterId);

    // ── Response DTOs (JSON estable para la UI) ────────────────────────

    public sealed record PatientDto(
        string Id, string FullName, string DocumentId, string Gender,
        string DateOfBirth, int AgeYears, string Phone, string Email, string City, string BloodType,
        IReadOnlyList<string> Allergies, IReadOnlyList<string> ChronicConditions,
        string? PrimaryDoctorId, string? AvatarUrl);

    public sealed record PatientsResponse(IReadOnlyList<PatientDto> Patients);

    public sealed record DoctorDto(
        string Id, string FullName, string Specialty, string LicenseNumber,
        double Rating, int YearsExperience, string? AvatarUrl,
        IReadOnlyList<int> WorkingDays, int SlotStartHour, int SlotEndHour, int SlotMinutes);

    public sealed record DoctorsResponse(IReadOnlyList<DoctorDto> Doctors);

    public sealed record VitalsDto(
        double? SystolicMmHg, double? DiastolicMmHg, double? HeartRateBpm, double? TemperatureC,
        double? WeightKg, double? HeightCm, double? GlucoseMgDl, double? OxygenSaturationPct);

    public sealed record SoapDto(string Subjective, string Objective, string Assessment, string Plan, VitalsDto? Vitals);

    public sealed record HistoryDto(
        string PatientId, string ChiefComplaint,
        IReadOnlyList<string> ActiveProblems, IReadOnlyList<string> PastMedicalHistory,
        IReadOnlyList<string> Allergies, IReadOnlyList<string> CurrentMedications,
        VitalsDto? BaselineVitals, DateTime? LastVisitUtc, int TotalEncounters);

    public sealed record EncounterDto(
        string Id, string PatientId, string DoctorId, string DoctorName,
        DateTime OccurredAtUtc, string ReasonForVisit, SoapDto Soap,
        string? DiagnosisCode, bool SignedByClinician);

    public sealed record EncounterEnvelope(EncounterDto Encounter);

    public sealed record PrescriptionItemDto(string MedicationName, string Dosage, string Frequency, int DurationDays, string? Instructions);

    public sealed record PrescriptionDto(
        string Id, string PatientId, string DoctorId, string DoctorName,
        DateTime IssuedAtUtc, string Status, string? EncounterId,
        IReadOnlyList<PrescriptionItemDto> Items);

    public sealed record PrescriptionEnvelope(PrescriptionDto Prescription);

    public sealed record AppointmentDto(
        string Id, string PatientId, string PatientName, string DoctorId, string DoctorName,
        string Specialty, DateTime StartUtc, DateTime EndUtc, string Status, string ReservationId);

    public sealed record AppointmentsResponse(IReadOnlyList<AppointmentDto> Appointments);

    public sealed record AppointmentEnvelope(AppointmentDto Appointment);

    public sealed record PatientChartResponse(
        PatientDto Patient,
        HistoryDto? History,
        IReadOnlyList<EncounterDto> Encounters,
        IReadOnlyList<PrescriptionDto> Prescriptions,
        IReadOnlyList<AppointmentDto> Appointments);
}
