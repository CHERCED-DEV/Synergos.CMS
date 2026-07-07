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
    /// <summary>Contexto de los hilos de mensajería del In Basket clínico (SH-7 v3).</summary>
    private const string ClinicalMessageContext = "clinical";

    private readonly IPatientRegistry _patients;
    private readonly IDoctorDirectory _doctors;
    private readonly IClinicalRecordService _records;
    private readonly IClinicalPrescriptionService _prescriptions;
    private readonly IClinicalSchedulingService _scheduling;
    private readonly IClinicalResultsProvider _results;
    private readonly IClinicalMedicationService _medications;
    private readonly IClinicalOrderService _orders;
    private readonly IClinicalBillingService _billing;
    private readonly IEhrInBasketService _inBasket;
    private readonly IMessagingService _messaging;

    public EhrController(
        IPatientRegistry patients,
        IDoctorDirectory doctors,
        IClinicalRecordService records,
        IClinicalPrescriptionService prescriptions,
        IClinicalSchedulingService scheduling,
        IClinicalResultsProvider results,
        IClinicalMedicationService medications,
        IClinicalOrderService orders,
        IClinicalBillingService billing,
        IEhrInBasketService inBasket,
        IMessagingService messaging)
    {
        _patients = patients;
        _doctors = doctors;
        _records = records;
        _prescriptions = prescriptions;
        _scheduling = scheduling;
        _results = results;
        _medications = medications;
        _orders = orders;
        _billing = billing;
        _inBasket = inBasket;
        _messaging = messaging;
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

    // ── OLA 7 · Portal paciente + clínico (doc 21 §2.5) ─────────────────

    // 8. Portal paciente — home
    // GET /api/ehr/portal/home?patient= → { nextAppointment, pendingTasks, unreadMessages, activeMeds }
    [HttpGet("portal/home")]
    public async Task<IActionResult> PortalHome([FromQuery] string? patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient))
        {
            return BadRequest(new { error = "El parámetro patient es requerido." });
        }

        var found = await _patients.GetAsync(patient, cancellationToken);
        if (found is null)
        {
            return NotFound(new { error = $"Paciente '{patient}' no encontrado." });
        }

        var appointments = await CollectPatientAppointmentsAsync(patient, cancellationToken);
        var now = DateTime.UtcNow;
        var next = appointments
            .Where(a => a.StartUtc >= now && !string.Equals(a.Status, "cancelled", StringComparison.Ordinal))
            .OrderBy(a => a.StartUtc)
            .FirstOrDefault();

        var meds = await _medications.GetActiveForPatientAsync(patient, cancellationToken);
        var results = await _results.GetForPatientAsync(patient, cancellationToken);
        var inbox = await _messaging.GetInboxAsync(patient, cancellationToken);

        // Tareas pendientes del paciente (demo): resultados anormales por revisar +
        // saldo pendiente. Derivadas, no un store paralelo.
        var pendingTasks = new List<PortalTaskDto>();
        var abnormal = results.Count(r => !string.Equals(r.Flag, "normal", StringComparison.OrdinalIgnoreCase));
        if (abnormal > 0)
        {
            pendingTasks.Add(new PortalTaskDto("review-results", $"Tienes {abnormal} resultado(s) por revisar", "results"));
        }
        var statement = await _billing.GetForPatientAsync(patient, cancellationToken);
        if (statement is not null && statement.Balance > 0)
        {
            pendingTasks.Add(new PortalTaskDto("pay-balance", "Tienes un saldo pendiente por pagar", "billing"));
        }

        return Ok(new PortalHomeResponse(
            NextAppointment: next is null ? null : ToAppointmentDto(next),
            PendingTasks: pendingTasks,
            UnreadMessages: inbox.Sum(t => t.MessageCount),
            ActiveMeds: meds.Select(ToMedicationDto).ToList()));
    }

    // 9. Resultados de laboratorio
    // GET /api/ehr/results?patient= → { results:[...] }
    [HttpGet("results")]
    public async Task<IActionResult> Results([FromQuery] string? patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient))
        {
            return BadRequest(new { error = "El parámetro patient es requerido." });
        }
        var results = await _results.GetForPatientAsync(patient, cancellationToken);
        return Ok(new LabResultsResponse(results.Select(ToLabResultDto).ToList()));
    }

    // 10. Medicamentos activos
    // GET /api/ehr/medications?patient= → { medications:[...] }
    [HttpGet("medications")]
    public async Task<IActionResult> Medications([FromQuery] string? patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient))
        {
            return BadRequest(new { error = "El parámetro patient es requerido." });
        }
        var meds = await _medications.GetActiveForPatientAsync(patient, cancellationToken);
        return Ok(new MedicationsResponse(meds.Select(ToMedicationDto).ToList()));
    }

    // 11. Solicitar refill
    // POST /api/ehr/refill { patient, medId, note? } → { refill }
    [HttpPost("refill")]
    public async Task<IActionResult> Refill([FromBody] RefillBody? body, CancellationToken cancellationToken)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Patient) || string.IsNullOrWhiteSpace(body.MedId))
        {
            return BadRequest(new { error = "patient y medId son requeridos." });
        }
        try
        {
            var refill = await _medications.RequestRefillAsync(
                new RefillRequest(body.Patient.Trim(), body.MedId.Trim(), body.Note),
                cancellationToken);
            return Ok(new RefillEnvelope(ToRefillDto(refill)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // 12. Mensajes (paciente ↔ equipo, contexto clinical)
    // GET /api/ehr/messages?user= → { threads:[...] }
    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] string? user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return BadRequest(new { error = "El parámetro user es requerido." });
        }
        var inbox = await _messaging.GetInboxAsync(user, cancellationToken);
        var clinical = inbox.Where(t => IsClinicalContext(t.ContextRef)).Select(ToThreadSummaryDto).ToList();
        return Ok(new MessagesResponse(clinical));
    }

    // 13. Enviar mensaje (paciente → equipo, contexto clinical)
    // POST /api/ehr/message { from, to, body, threadId? } → { thread }
    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageBody? body, CancellationToken cancellationToken)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.From) || string.IsNullOrWhiteSpace(body.Body))
        {
            return BadRequest(new { error = "from y body son requeridos." });
        }

        try
        {
            MessageThread thread;
            if (!string.IsNullOrWhiteSpace(body.ThreadId))
            {
                // Respuesta a un hilo existente.
                thread = await _messaging.ReplyAsync(body.ThreadId.Trim(), body.From.Trim(), body.Body, cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.To))
                {
                    return BadRequest(new { error = "to (o threadId) es requerido para iniciar un hilo." });
                }
                // Nuevo hilo clínico: contexto namespaced para que el In Basket lo reconozca.
                var contextRef = $"{ClinicalMessageContext}:msg:{body.From.Trim()}:{body.To.Trim()}";
                thread = await _messaging.StartThreadAsync(contextRef, body.From.Trim(), body.To.Trim(), body.Body, cancellationToken);
            }
            return Ok(new ThreadEnvelope(ToThreadDto(thread)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // 14. In Basket del proveedor (cola tipada)
    // GET /api/ehr/inbasket?provider=&type= → { items:[...] }
    [HttpGet("inbasket")]
    public async Task<IActionResult> InBasket([FromQuery] string? provider, [FromQuery] string? type, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return BadRequest(new { error = "El parámetro provider es requerido." });
        }
        var items = await _inBasket.GetForProviderAsync(provider, type, cancellationToken);
        return Ok(new InBasketResponse(items.Select(ToInBasketDto).ToList()));
    }

    // 15. Colocar orden clínica (lab / eRx / imaging / referral)
    // POST /api/ehr/order { patient, provider, type, detail } → { order }
    [HttpPost("order")]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderBody? body, CancellationToken cancellationToken)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.Patient)
            || string.IsNullOrWhiteSpace(body.Provider)
            || string.IsNullOrWhiteSpace(body.Type)
            || string.IsNullOrWhiteSpace(body.Detail))
        {
            return BadRequest(new { error = "patient, provider, type y detail son requeridos." });
        }
        try
        {
            var order = await _orders.PlaceAsync(
                new PlaceOrderRequest(body.Patient.Trim(), body.Provider.Trim(), body.Type.Trim(), body.Detail),
                cancellationToken);
            return Ok(new OrderEnvelope(ToOrderDto(order)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // 16. Facturación del paciente
    // GET /api/ehr/billing?patient= → { statement, balance, currency, plan }
    [HttpGet("billing")]
    public async Task<IActionResult> Billing([FromQuery] string? patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient))
        {
            return BadRequest(new { error = "El parámetro patient es requerido." });
        }
        var statement = await _billing.GetForPatientAsync(patient, cancellationToken);
        if (statement is null)
        {
            return NotFound(new { error = $"Paciente '{patient}' no encontrado." });
        }
        return Ok(ToBillingDto(statement));
    }

    // 17. Tablero clínico del día (schedule board con máquina de estados)
    // GET /api/ehr/schedule?date= → { slots:[...] } (deriva de la agenda viva)
    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] string? date, CancellationToken cancellationToken)
    {
        var day = ParseDateOrToday(date);
        var appts = await _scheduling.GetByDateAsync(day, doctorId: null, cancellationToken);
        var now = DateTime.UtcNow;
        var slots = appts
            .OrderBy(a => a.StartUtc)
            .Select(a => new ScheduleSlotDto(
                AppointmentId: a.Id,
                PatientId: a.PatientId,
                PatientName: a.PatientName,
                DoctorId: a.DoctorId,
                DoctorName: a.DoctorName,
                Time: a.StartUtc.ToString("HH:mm"),
                DurationMin: Math.Max(0, (int)(a.EndUtc - a.StartUtc).TotalMinutes),
                Reason: string.IsNullOrWhiteSpace(a.Specialty) ? "Consulta" : a.Specialty,
                Type: "in-person",
                State: DeriveScheduleState(a.StartUtc, a.EndUtc, now),
                CheckedInAhead: (Math.Abs(a.Id.GetHashCode()) % 2) == 0))
            .ToList();
        return Ok(new ScheduleResponse(slots));
    }

    // 18. Resumen de salud del paciente (Mi salud): condiciones/alergias/vacunas/preventivo
    // GET /api/ehr/health?patient= → { conditions, allergies, immunizations, maintenance }
    [HttpGet("health")]
    public async Task<IActionResult> Health([FromQuery] string? patient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patient))
        {
            return BadRequest(new { error = "El parámetro patient es requerido." });
        }
        var person = await _patients.GetAsync(patient, cancellationToken);
        if (person is null)
        {
            return NotFound(new { error = $"Paciente '{patient}' no encontrado." });
        }
        var history = await _records.GetHistoryAsync(patient, cancellationToken);

        // Condiciones/alergias son datos REALES del registro; vacunas y cuidado
        // preventivo se derivan de forma determinista (capa demo, coherente con la
        // tarjeta "Cuidado preventivo" del home).
        var conditions = (history?.ActiveProblems?.Count > 0 ? history.ActiveProblems : person.ChronicConditions)
            ?? Array.Empty<string>();
        var allergies = (history?.Allergies?.Count > 0 ? history.Allergies : person.Allergies)
            ?? Array.Empty<string>();

        var seed = Math.Abs(person.Id.GetHashCode());
        var immunizations = new List<ImmunizationDto>
        {
            new($"imm-flu-{person.Id}", "Influenza (anual)", ClinicRelDate(-8 - (seed % 4)), (seed % 3) == 0 ? "due" : "complete"),
            new($"imm-covid-{person.Id}", "COVID-19 (refuerzo)", ClinicRelDate(-14 - (seed % 6)), (seed % 2) == 0 ? "complete" : "due"),
            new($"imm-tdap-{person.Id}", "Tétanos/difteria (Td)", ClinicRelDate(-60 - (seed % 24)), person.AgeYears >= 50 ? "overdue" : "complete"),
        };
        var maintenance = new List<HealthMaintenanceDto>();
        if (person.AgeYears >= 45)
        {
            maintenance.Add(new($"pm-colon-{person.Id}", "Tamizaje de colon",
                "Colonoscopia o prueba de sangre oculta según riesgo.", person.AgeYears >= 50 ? "overdue" : "due", ClinicRelDate(-2)));
        }
        maintenance.Add(new($"pm-bp-{person.Id}", "Control de presión arterial",
            "Toma de presión en consulta de control.", (seed % 2) == 0 ? "due" : "complete", ClinicRelDate(1)));
        if (string.Equals(person.Gender, "F", StringComparison.OrdinalIgnoreCase) && person.AgeYears >= 40)
        {
            maintenance.Add(new($"pm-mammo-{person.Id}", "Mamografía",
                "Tamizaje de mama bienal.", "due", ClinicRelDate(3)));
        }

        return Ok(new HealthSummaryResponse(
            Conditions: conditions,
            Allergies: allergies,
            Immunizations: immunizations,
            Maintenance: maintenance));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Estado de llegada determinista a partir de la hora de la cita vs. ahora.
    private static string DeriveScheduleState(DateTime startUtc, DateTime endUtc, DateTime now)
    {
        if (now >= endUtc) { return "checked-out"; }
        if (now >= startUtc) { return "in-visit"; }
        var minutesToStart = (startUtc - now).TotalMinutes;
        if (minutesToStart <= 30) { return "roomed"; }
        if (minutesToStart <= 60) { return "arrived"; }
        return "scheduled";
    }

    // Fecha relativa (meses respecto a hoy UTC) en formato yyyy-MM-dd para la demo.
    private static string ClinicRelDate(int months)
        => DateTime.UtcNow.AddMonths(months).ToString("yyyy-MM-dd");

    private static bool IsClinicalContext(string contextRef)
        => !string.IsNullOrEmpty(contextRef)
            && (string.Equals(contextRef, ClinicalMessageContext, StringComparison.Ordinal)
                || contextRef.StartsWith($"{ClinicalMessageContext}:", StringComparison.Ordinal));

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

    // ── OLA 7 mappers ───────────────────────────────────────────────────

    private static LabResultDto ToLabResultDto(EhrLabResult r) => new(
        Id: r.Id, PatientId: r.PatientId, PanelName: r.PanelName, TestName: r.TestName,
        Value: r.Value, Unit: r.Unit, ReferenceLow: r.ReferenceLow, ReferenceHigh: r.ReferenceHigh,
        Flag: r.Flag, ResultedAtUtc: r.ResultedAtUtc, OrderId: r.OrderId,
        OrderedByDoctorId: r.OrderedByDoctorId, Notes: r.Notes);

    private static MedicationDto ToMedicationDto(EhrMedication m) => new(
        MedicationId: m.MedicationId, PatientId: m.PatientId, MedicationName: m.MedicationName,
        Dosage: m.Dosage, Frequency: m.Frequency, Instructions: m.Instructions,
        PrescribedByDoctorId: m.PrescribedByDoctorId, PrescribedByDoctorName: m.PrescribedByDoctorName,
        PrescribedAtUtc: m.PrescribedAtUtc, Status: m.Status, RefillsRemaining: m.RefillsRemaining);

    private static RefillDto ToRefillDto(EhrRefillRequest r) => new(
        Id: r.Id, PatientId: r.PatientId, MedicationId: r.MedicationId, MedicationName: r.MedicationName,
        ProviderId: r.ProviderId, RequestedAtUtc: r.RequestedAtUtc, Status: r.Status, Note: r.Note);

    private static OrderDto ToOrderDto(EhrClinicalOrder o) => new(
        Id: o.Id, PatientId: o.PatientId, ProviderId: o.ProviderId, ProviderName: o.ProviderName,
        Type: o.Type, Detail: o.Detail, PlacedAtUtc: o.PlacedAtUtc, Status: o.Status);

    private static InBasketItemDto ToInBasketDto(EhrInBasketItem i) => new(
        Id: i.Id, Type: i.Type, ProviderId: i.ProviderId, PatientId: i.PatientId,
        PatientName: i.PatientName, Title: i.Title, Preview: i.Preview, Priority: i.Priority,
        OccurredAtUtc: i.OccurredAtUtc, RefId: i.RefId);

    private static ThreadSummaryDto ToThreadSummaryDto(MessageThreadSummary s) => new(
        ThreadId: s.ThreadId, ContextRef: s.ContextRef, Participants: s.Participants,
        LastMessagePreview: s.LastMessagePreview, LastMessageAt: s.LastMessageAt.UtcDateTime,
        MessageCount: s.MessageCount);

    private static ThreadDto ToThreadDto(MessageThread t) => new(
        ThreadId: t.ThreadId, ContextRef: t.ContextRef, Participants: t.Participants,
        Messages: t.Messages.Select(m => new ThreadMessageDto(m.MessageId, m.From, m.Body, m.SentAt.UtcDateTime)).ToList(),
        CreatedAt: t.CreatedAt.UtcDateTime, LastMessageAt: t.LastMessageAt.UtcDateTime);

    private static BillingDto ToBillingDto(EhrBillingStatement s) => new(
        PatientId: s.PatientId,
        Statement: s.Statement.Select(l => new BillingLineDto(
            l.Id, l.ServiceDateUtc, l.Description, l.Amount, l.PatientResponsibility, l.Status)).ToList(),
        Balance: s.Balance, Currency: s.Currency,
        Plan: new InsurancePlanDto(s.Plan.PlanName, s.Plan.MemberId, s.Plan.Coverage, s.Plan.CopayAmount));

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

    // ── OLA 7 · Request bodies ──────────────────────────────────────────

    public sealed record RefillBody(string Patient, string MedId, string? Note);

    public sealed record SendMessageBody(string From, string? To, string Body, string? ThreadId);

    public sealed record PlaceOrderBody(string Patient, string Provider, string Type, string Detail);

    // ── OLA 7 · Response DTOs ───────────────────────────────────────────

    public sealed record PortalTaskDto(string Key, string Label, string Target);

    public sealed record PortalHomeResponse(
        AppointmentDto? NextAppointment,
        IReadOnlyList<PortalTaskDto> PendingTasks,
        int UnreadMessages,
        IReadOnlyList<MedicationDto> ActiveMeds);

    public sealed record ScheduleSlotDto(
        string AppointmentId, string PatientId, string PatientName, string DoctorId, string DoctorName,
        string Time, int DurationMin, string Reason, string Type, string State, bool CheckedInAhead);

    public sealed record ScheduleResponse(IReadOnlyList<ScheduleSlotDto> Slots);

    public sealed record ImmunizationDto(string Id, string Name, string Date, string Status);

    public sealed record HealthMaintenanceDto(string Id, string Name, string Detail, string Status, string DueDate);

    public sealed record HealthSummaryResponse(
        IReadOnlyList<string> Conditions,
        IReadOnlyList<string> Allergies,
        IReadOnlyList<ImmunizationDto> Immunizations,
        IReadOnlyList<HealthMaintenanceDto> Maintenance);

    public sealed record LabResultDto(
        string Id, string PatientId, string PanelName, string TestName,
        string Value, string Unit, double? ReferenceLow, double? ReferenceHigh,
        string Flag, DateTime ResultedAtUtc, string? OrderId, string? OrderedByDoctorId, string? Notes);

    public sealed record LabResultsResponse(IReadOnlyList<LabResultDto> Results);

    public sealed record MedicationDto(
        string MedicationId, string PatientId, string MedicationName, string Dosage, string Frequency,
        string? Instructions, string PrescribedByDoctorId, string PrescribedByDoctorName,
        DateTime PrescribedAtUtc, string Status, int? RefillsRemaining);

    public sealed record MedicationsResponse(IReadOnlyList<MedicationDto> Medications);

    public sealed record RefillDto(
        string Id, string PatientId, string MedicationId, string MedicationName,
        string ProviderId, DateTime RequestedAtUtc, string Status, string? Note);

    public sealed record RefillEnvelope(RefillDto Refill);

    public sealed record OrderDto(
        string Id, string PatientId, string ProviderId, string ProviderName,
        string Type, string Detail, DateTime PlacedAtUtc, string Status);

    public sealed record OrderEnvelope(OrderDto Order);

    public sealed record InBasketItemDto(
        string Id, string Type, string ProviderId, string PatientId, string PatientName,
        string Title, string Preview, string Priority, DateTime OccurredAtUtc, string RefId);

    public sealed record InBasketResponse(IReadOnlyList<InBasketItemDto> Items);

    public sealed record ThreadMessageDto(string MessageId, string From, string Body, DateTime SentAt);

    public sealed record ThreadDto(
        string ThreadId, string ContextRef, IReadOnlyList<string> Participants,
        IReadOnlyList<ThreadMessageDto> Messages, DateTime CreatedAt, DateTime LastMessageAt);

    public sealed record ThreadEnvelope(ThreadDto Thread);

    public sealed record ThreadSummaryDto(
        string ThreadId, string ContextRef, IReadOnlyList<string> Participants,
        string LastMessagePreview, DateTime LastMessageAt, int MessageCount);

    public sealed record MessagesResponse(IReadOnlyList<ThreadSummaryDto> Threads);

    public sealed record BillingLineDto(
        string Id, DateTime ServiceDateUtc, string Description, decimal Amount,
        decimal PatientResponsibility, string Status);

    public sealed record InsurancePlanDto(string PlanName, string MemberId, string Coverage, decimal CopayAmount);

    public sealed record BillingDto(
        string PatientId, IReadOnlyList<BillingLineDto> Statement,
        decimal Balance, string Currency, InsurancePlanDto Plan);
}
