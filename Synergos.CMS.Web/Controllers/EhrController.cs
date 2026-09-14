using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Filters;

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
/// <strong>DEV-ONLY (<see cref="DevSeedOnlyAttribute"/>, ADR 0013).</strong> Estos
/// endpoints son anónimos a propósito — la premisa del demo es entrar y caer en el
/// panel sin login. Eso es aceptable SOLO porque la data es fabricada
/// (<c>EhrDemoSeed</c>, 5 pacientes de mentira) y porque el flag los hace 404 fuera
/// de dev. Las dos mitades de esa frase se sostienen mutuamente:
/// <list type="bullet">
/// <item><strong>PHI real NUNCA pasa por aquí.</strong> Va por <c>/api/healthcare</c>,
///   que gatea con <see cref="IPhiAccessGuard"/> (rol + pertenencia + consentimiento,
///   auditado, fail-closed). Enchufar un adapter HIS/DB real detrás de estos seams
///   publicaría el censo entero a cualquier anónimo, con build verde y sin que nadie
///   toque este archivo.</item>
/// <item>Si algún día este dashboard debe servir pacientes reales, NO basta con pedir
///   login: <c>patientId</c>/<c>patient</c>/<c>user</c>/<c>provider</c> los pone el
///   caller, así que cualquier member autenticado leería historias ajenas (el mismo
///   IDOR que T2 cerró en Tienda). Exige partir las dos superficies que hoy conviven
///   aquí — la clínica (rol) y el portal del paciente (pertenencia) — y es una
///   decisión de arquitectura, no un parche.</item>
/// </list>
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
[DevSeedOnly]
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

        // `history` es la LISTA DE NOTAS, no el resumen. La pestaña «Notas» de la historia
        // clínica lee `chart.history[]`; mientras esta clave llevó el resumen (un objeto), esa
        // pestaña salía VACÍA contra el servidor y llena contra el mock — o sea que el clínico
        // veía notas de ejemplo donde debía ver las del paciente. El resumen no se pierde: pasa
        // a `clinicalHistory`, porque el mismo nombre no puede significar dos cosas.
        var notes = encounters.Select(ToEncounterDto).ToList();
        return Ok(new PatientChartResponse(
            Patient: ToPatientDto(patient),
            History: notes,
            Encounters: notes,
            Prescriptions: prescriptions.Select(ToPrescriptionDto).ToList(),
            Appointments: appointments.Select(ToAppointmentDto).ToList(),
            ClinicalHistory: history is null ? null : ToHistoryDto(history)));
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
        if (body.ResolveSlotUtc() is not { } slot)
        {
            return BadRequest(new { error = "slot (fecha/hora UTC, o { date, time }) es requerido y debe ser válido." });
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
                        Objective: body.Soap.ObjectiveText(),
                        Assessment: body.Soap.Assessment ?? string.Empty,
                        Plan: body.Soap.Plan ?? string.Empty,
                        // Los signos vitales salen de `objective` cuando llega como objeto —que es
                        // como este mismo borde los DEVUELVE— y de `vitals` cuando vienen aparte.
                        Vitals: body.Soap.ResolveVitals()),
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
                        MedicationName: i.ResolveDrug(),
                        Dosage: i.ResolveDose(),
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

        var results = await _results.GetForPatientAsync(patient, cancellationToken);
        var inbox = await _messaging.GetInboxAsync(patient, cancellationToken);
        var statement = await _billing.GetForPatientAsync(patient, cancellationToken);

        var unreadMessages = inbox.Where(t => IsClinicalContext(t.ContextRef)).Sum(t => t.MessageCount);
        var balanceMinor = statement is null ? 0L : (long)decimal.Truncate(Math.Max(0m, statement.Balance));
        var currency = statement?.Currency ?? "COP";

        // e-Check-In disponible = próxima cita en ventana de 48h sin check-in previo.
        var pendingCheckins = next is not null
            && !string.Equals(next.Status, "checked-in", StringComparison.OrdinalIgnoreCase)
            && (next.StartUtc - now).TotalHours <= 48
                ? 1 : 0;

        // Cards del home derivadas de datos VIVOS (deep-link a las vistas del portal).
        var cards = new List<HomeCardDto>();

        if (next is not null)
        {
            cards.Add(new HomeCardDto(
                Id: $"card-appt-{next.Id}", Kind: "appointment",
                Title: "Tu próxima cita",
                Detail: $"{next.StartUtc:yyyy-MM-dd HH:mm} · {(string.IsNullOrWhiteSpace(next.Specialty) ? "Consulta" : next.Specialty)} · {next.DoctorName}",
                Action: "visits", ActionLabel: "Ver cita", Tone: "brand"));

            if (pendingCheckins > 0)
            {
                cards.Add(new HomeCardDto(
                    Id: $"card-checkin-{next.Id}", Kind: "checkin",
                    Title: "e-Check-In disponible",
                    Detail: "Completa tu registro antes de llegar y ahorra tiempo en recepción.",
                    Action: "echeckin", ActionLabel: "Hacer check-in", Tone: "success"));
            }
        }

        var latestResult = results.OrderByDescending(r => r.ResultedAtUtc).FirstOrDefault();
        if (latestResult is not null)
        {
            var abnormal = results.Count(r => !string.Equals(r.Flag, "normal", StringComparison.OrdinalIgnoreCase));
            cards.Add(new HomeCardDto(
                Id: $"card-result-{latestResult.Id}", Kind: "result",
                Title: abnormal > 0 ? "Resultados por revisar" : "Nuevo resultado disponible",
                Detail: abnormal > 0
                    ? $"Tienes {abnormal} resultado(s) fuera de rango. Revísalos con tu médico."
                    : $"Tu último resultado ({latestResult.TestName}) ya está disponible.",
                Action: "results", ActionLabel: "Ver resultados",
                Tone: abnormal > 0 ? "warning" : "success"));
        }

        if (unreadMessages > 0)
        {
            cards.Add(new HomeCardDto(
                Id: "card-messages", Kind: "message",
                Title: "Mensajes de tu equipo de salud",
                Detail: $"Tienes {unreadMessages} mensaje(s) en tu bandeja.",
                Action: "messages", ActionLabel: "Abrir mensajes", Tone: "brand"));
        }

        if (balanceMinor > 0)
        {
            cards.Add(new HomeCardDto(
                Id: "card-balance", Kind: "balance",
                Title: "Saldo pendiente",
                Detail: $"Tienes un saldo de {balanceMinor:N0} {currency} por pagar.",
                Action: "billing", ActionLabel: "Pagar ahora", Tone: "warning"));
        }

        // Cuidado preventivo — card estable de recordatorio (siempre útil).
        cards.Add(new HomeCardDto(
            Id: "card-reminder-health", Kind: "reminder",
            Title: "Cuidado preventivo",
            Detail: "Revisa tus vacunas y tamizajes al día en tu resumen de salud.",
            Action: "health", ActionLabel: "Ver mi salud", Tone: "neutral"));

        return Ok(new PortalHomeResponse(
            Patient: ToPatientDto(found),
            Cards: cards,
            NextAppointment: next is null ? null : ToAppointmentDto(next),
            BalanceMinor: balanceMinor,
            Currency: currency,
            UnreadMessages: unreadMessages,
            PendingCheckins: pendingCheckins));
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
        var refillPatient = body?.ResolvePatient() ?? string.Empty;
        var refillMedication = body?.ResolveMedication() ?? string.Empty;
        if (string.IsNullOrEmpty(refillPatient) || string.IsNullOrEmpty(refillMedication))
        {
            return BadRequest(new { error = "patientId (patient) y medicationId (medId) son requeridos." });
        }
        try
        {
            var refill = await _medications.RequestRefillAsync(
                new RefillRequest(refillPatient, refillMedication, body!.Note),
                cancellationToken);
            // La UI lee `status` top-level (requested|approved|denied); el seam usa
            // 'pending' para una solicitud recién creada → 'requested'.
            return Ok(new RefillEnvelope(RefillLifecycleStatus(refill.Status), ToRefillDto(refill)));
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
        var clinical = inbox.Where(t => IsClinicalContext(t.ContextRef)).ToList();

        // La UI espera hilos COMPLETOS (con mensajes) — el inbox solo trae resúmenes,
        // así que hidratamos cada hilo con GetThreadAsync. Best-effort: si un hilo se
        // fue, cae al shape del resumen (messages vacío) sin romper.
        var threads = new List<ThreadDto>(clinical.Count);
        foreach (var summary in clinical)
        {
            var full = await _messaging.GetThreadAsync(summary.ThreadId, cancellationToken);
            threads.Add(full is null ? ToThreadDtoFromSummary(summary, user) : ToThreadDto(full, user));
        }
        return Ok(new MessagesResponse(threads));
    }

    // 13. Enviar mensaje (paciente → equipo, contexto clinical)
    // POST /api/ehr/message { from, to, body, threadId? } → { thread }
    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageBody? body, CancellationToken cancellationToken)
    {
        var sender = body?.ResolveFrom() ?? string.Empty;
        if (string.IsNullOrEmpty(sender) || string.IsNullOrWhiteSpace(body!.Body))
        {
            return BadRequest(new { error = "user (from) y body son requeridos." });
        }

        try
        {
            MessageThread thread;
            if (!string.IsNullOrWhiteSpace(body.ThreadId))
            {
                // Respuesta a un hilo existente.
                thread = await _messaging.ReplyAsync(body.ThreadId.Trim(), sender, body.Body, cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.To))
                {
                    return BadRequest(new { error = "to (o threadId) es requerido para iniciar un hilo." });
                }
                // Nuevo hilo clínico: contexto namespaced para que el In Basket lo reconozca.
                var contextRef = $"{ClinicalMessageContext}:msg:{sender}:{body.To.Trim()}";
                thread = await _messaging.StartThreadAsync(contextRef, sender, body.To.Trim(), body.Body, cancellationToken);
            }
            return Ok(new ThreadEnvelope(ToThreadDto(thread, sender)));
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
        var orderPatient = body?.ResolvePatient() ?? string.Empty;
        var orderType = body?.ResolveType() ?? string.Empty;
        if (string.IsNullOrEmpty(orderPatient)
            || string.IsNullOrEmpty(orderType)
            || string.IsNullOrWhiteSpace(body!.Detail))
        {
            return BadRequest(new { error = "patientId (patient), kind (type) y detail son requeridos." });
        }

        // El prescriptor no viaja desde la UI. En vez de inventarlo, se resuelve al médico
        // tratante del padrón; si el paciente no tiene, se dice, porque una orden clínica sin
        // quien la firma no es una orden.
        var orderProvider = string.IsNullOrWhiteSpace(body.Provider)
            ? ((await _patients.GetAsync(orderPatient, cancellationToken))?.PrimaryDoctorId ?? string.Empty)
            : body.Provider.Trim();
        if (string.IsNullOrWhiteSpace(orderProvider))
        {
            return BadRequest(new { error = "provider es requerido: el paciente no tiene médico tratante registrado." });
        }

        try
        {
            var order = await _orders.PlaceAsync(
                new PlaceOrderRequest(orderPatient, orderProvider, orderType, body.Detail),
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
        Id: p.Id, Name: p.FullName, Document: p.DocumentId, Sex: NormalizeSex(p.Gender),
        Age: p.AgeYears, Phone: p.Phone, Email: p.Email, BloodType: p.BloodType,
        Problems: p.ChronicConditions, Allergies: p.Allergies,
        PrimaryDoctorId: p.PrimaryDoctorId ?? string.Empty, Active: true,
        City: p.City, AvatarUrl: p.AvatarUrl);

    // El registro modela el sexo como texto libre ("Masculino"/"Femenino"/…); la UI
    // (readSex) espera el código 'M'|'F'|'X'. Mapeo por inicial, case-insensitive.
    private static string NormalizeSex(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) { return "X"; }
        return char.ToUpperInvariant(gender.TrimStart()[0]) switch
        {
            'M' => "M",
            'F' => "F",
            _ => "X",
        };
    }

    private static DoctorDto ToDoctorDto(MedicalDoctor d) => new(
        Id: d.Id, Name: d.FullName, Specialty: d.Specialty, License: d.LicenseNumber,
        Phone: string.Empty, Email: string.Empty, AcceptingPatients: true,
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
        Date: e.OccurredAtUtc.ToString("yyyy-MM-dd"), Reason: e.ReasonForVisit,
        Soap: new SoapDto(
            Subjective: e.Soap.Subjective,
            Objective: ToVitalsDto(e.Soap.Vitals),
            Assessment: e.Soap.Assessment,
            Plan: e.Soap.Plan),
        // e.Soap.Objective (texto libre) va como firma-legible no; la firma = iniciales
        // del clínico si el encuentro está firmado (la UI muestra la firma como string).
        Signature: e.SignedByClinician ? ClinicianInitials(e.DoctorName) : string.Empty);

    // Iniciales del clínico para la "firma" que muestra la UI (data-url o iniciales).
    private static string ClinicianInitials(string doctorName)
    {
        if (string.IsNullOrWhiteSpace(doctorName)) { return string.Empty; }
        var parts = doctorName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.EndsWith('.') && p.Length > 1).ToList();
        return parts.Count == 0
            ? string.Empty
            : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    private static PrescriptionDto ToPrescriptionDto(EhrPrescription p) => new(
        Id: p.Id, PatientId: p.PatientId, DoctorId: p.DoctorId, DoctorName: p.DoctorName,
        Date: p.IssuedAtUtc.ToString("yyyy-MM-dd"),
        Items: p.Items.Select(i => new PrescriptionItemDto(
            Drug: i.MedicationName, Dose: i.Dosage, Frequency: i.Frequency, DurationDays: i.DurationDays)).ToList(),
        Interactions: Array.Empty<string>());

    private static AppointmentDto ToAppointmentDto(ClinicalAppointment a)
    {
        var date = a.StartUtc.ToString("yyyy-MM-dd");
        var time = a.StartUtc.ToString("HH:mm");
        return new AppointmentDto(
            Id: a.Id, PatientId: a.PatientId, PatientName: a.PatientName,
            DoctorId: a.DoctorId, DoctorName: a.DoctorName,
            Date: date, Time: time,
            DurationMin: Math.Max(0, (int)(a.EndUtc - a.StartUtc).TotalMinutes),
            Reason: string.IsNullOrWhiteSpace(a.Specialty) ? "Consulta" : a.Specialty,
            Status: a.Status,
            Slot: new AppointmentSlotDto(date, time),
            ReservationId: a.ReservationId);
    }

    // Vitals con las claves que espera la UI (systolic/diastolic/heartRate/…). Null-safe:
    // vitals ausentes → todos 0 (el normalizer de la UI también defaultea a 0).
    private static VitalsDto ToVitalsDto(ClinicalVitals? v) => new(
        Systolic: v?.SystolicMmHg ?? 0, Diastolic: v?.DiastolicMmHg ?? 0,
        HeartRate: v?.HeartRateBpm ?? 0, Temperature: v?.TemperatureC ?? 0,
        Weight: v?.WeightKg ?? 0, Height: v?.HeightCm ?? 0, Glucose: v?.GlucoseMgDl ?? 0);

    // ── OLA 7 mappers ───────────────────────────────────────────────────

    private static LabResultDto ToLabResultDto(EhrLabResult r) => new(
        Id: r.Id, PatientId: r.PatientId, Panel: r.PanelName, Name: r.TestName,
        Value: r.Value, Unit: r.Unit, RefLow: r.ReferenceLow, RefHigh: r.ReferenceHigh,
        Flag: r.Flag, Date: r.ResultedAtUtc.ToString("yyyy-MM-dd"), Released: true,
        Comment: r.Notes ?? string.Empty);

    /// <summary>Farmacia de la demo (el seam no modela la farmacia dispensadora aún).</summary>
    private const string DemoPharmacy = "Farmacia Synergos";

    private static MedicationDto ToMedicationDto(EhrMedication m) => new(
        Id: m.MedicationId, PatientId: m.PatientId, Drug: m.MedicationName,
        Dose: m.Dosage, Frequency: m.Frequency, Instructions: m.Instructions ?? string.Empty,
        Pharmacy: DemoPharmacy, RefillsLeft: m.RefillsRemaining ?? 0, RefillStatus: null);

    // Mapea el estado del seam (pending|approved|denied) al lifecycle que espera la UI.
    private static string RefillLifecycleStatus(string seamStatus) => seamStatus switch
    {
        "approved" => "approved",
        "denied" => "denied",
        _ => "requested",
    };

    private static RefillDto ToRefillDto(EhrRefillRequest r) => new(
        Id: r.Id, PatientId: r.PatientId, MedicationId: r.MedicationId, MedicationName: r.MedicationName,
        ProviderId: r.ProviderId, RequestedAtUtc: r.RequestedAtUtc, Status: r.Status, Note: r.Note);

    private static OrderDto ToOrderDto(EhrClinicalOrder o) => new(
        Id: o.Id, PatientId: o.PatientId, ProviderId: o.ProviderId, ProviderName: o.ProviderName,
        Type: o.Type, Detail: o.Detail, PlacedAtUtc: o.PlacedAtUtc, Status: o.Status);

    // El seam tipa result|refill|message; la UI espera result|refill|advice|cosign.
    // 'message' del paciente = 'advice' en la bandeja del clínico.
    private static string ToInBasketKind(string type) => type switch
    {
        "result" => "result",
        "refill" => "refill",
        "cosign" => "cosign",
        _ => "advice",
    };

    private static InBasketItemDto ToInBasketDto(EhrInBasketItem i) => new(
        Id: i.Id, Kind: ToInBasketKind(i.Type), PatientId: i.PatientId,
        PatientName: i.PatientName, Title: i.Title, Detail: i.Preview,
        Priority: i.Priority, CreatedAtUtc: i.OccurredAtUtc, Done: false);

    // El "otro" participante del hilo (la contraparte del usuario que consulta).
    private static string OtherParticipant(IReadOnlyList<string> participants, string self)
        => participants.FirstOrDefault(p => !string.Equals(p, self, StringComparison.OrdinalIgnoreCase))
            ?? (participants.Count > 0 ? participants[0] : string.Empty);

    // Asunto legible del hilo a partir del contextRef namespaced ('clinical:refill:…').
    private static string ThreadSubject(string contextRef)
    {
        if (string.IsNullOrWhiteSpace(contextRef)) { return "Mensaje"; }
        var parts = contextRef.Split(':');
        return parts.Length >= 2 ? parts[1] switch
        {
            "refill" => "Solicitud de resurtido",
            "result" => "Resultado de laboratorio",
            "advice" => "Consejo médico",
            "msg" => "Mensaje al equipo de salud",
            _ => "Mensaje clínico",
        } : "Mensaje clínico";
    }

    private static ThreadDto ToThreadDto(MessageThread t, string self)
    {
        var messages = t.Messages
            .Select(m => new ThreadMessageDto(
                Id: m.MessageId, Author: m.From, Body: m.Body,
                CreatedAtUtc: m.SentAt.UtcDateTime,
                Outgoing: string.Equals(m.From, self, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var last = t.Messages.Count > 0 ? t.Messages[^1].Body : string.Empty;
        return new ThreadDto(
            Id: t.ThreadId,
            Participant: OtherParticipant(t.Participants, self),
            Subject: ThreadSubject(t.ContextRef),
            LastMessage: last,
            LastAtUtc: t.LastMessageAt.UtcDateTime,
            Unread: 0,
            Messages: messages);
    }

    // Fallback cuando solo tenemos el resumen (hilo no rehidratable): shape completo
    // con messages vacío (el normalizer de la UI lo tolera).
    private static ThreadDto ToThreadDtoFromSummary(MessageThreadSummary s, string self) => new(
        Id: s.ThreadId,
        Participant: OtherParticipant(s.Participants, self),
        Subject: ThreadSubject(s.ContextRef),
        LastMessage: s.LastMessagePreview,
        LastAtUtc: s.LastMessageAt.UtcDateTime,
        Unread: 0,
        Messages: Array.Empty<ThreadMessageDto>());

    private static BillingDto ToBillingDto(EhrBillingStatement s)
    {
        // amountMinor = responsabilidad del paciente por línea (lo que debe/pagó);
        // COP no tiene subdivisión menor, así que la unidad menor entera = el monto COP.
        var lines = s.Statement
            .Select(l => new BillingLineDto(
                Id: l.Id,
                Date: l.ServiceDateUtc.ToString("yyyy-MM-dd"),
                Description: l.Description,
                AmountMinor: (long)decimal.Truncate(l.PatientResponsibility)))
            .ToList();
        var statement = new BillingStatementDto(
            PatientId: s.PatientId,
            Currency: s.Currency,
            BalanceMinor: (long)decimal.Truncate(s.Balance),
            PlanActive: !string.IsNullOrWhiteSpace(s.Plan.PlanName),
            Lines: lines);
        return new BillingDto(statement);
    }

    // ── Lectura tolerante del cuerpo (ADR 0083: la UI es la fuente del contrato) ──
    //
    // Los cuerpos de abajo declaran DOS juegos de claves: el que este borde estrenó y el que
    // la UI manda. No es cortesía — `System.Text.Json` DESCARTA en silencio lo que no mapea,
    // así que una clave que falta no es un error visible: es un campo vacío corriente abajo,
    // o un 400 constante que el cliente tapa con su valor optimista. Se prefiere siempre la
    // clave de la UI y se cae a la legacy.

    private static readonly JsonSerializerOptions VitalsJson = new(JsonSerializerDefaults.Web);

    private static string FirstNonBlank(string? preferred, string? legacy)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) { return preferred.Trim(); }
        return string.IsNullOrWhiteSpace(legacy) ? string.Empty : legacy.Trim();
    }

    /// <summary>
    /// El inicio del slot, venga como instante ISO o como el objeto <c>{ date, time }</c> que
    /// este mismo borde devuelve en <see cref="AppointmentDto.Slot"/>.
    /// </summary>
    private static DateTime? ReadSlot(JsonElement? slot)
    {
        if (slot is not { } value)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.TryGetDateTime(out var instant) ? instant : null;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var date = value.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
        var time = value.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        // Sin hora, el slot es el arranque del día; la agenda rechazará lo que no sea suyo.
        var composed = string.IsNullOrWhiteSpace(time) ? date : $"{date}T{time}";
        return DateTime.TryParse(
            composed,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    // ── Request bodies (binding del módulo UI) ─────────────────────────

    /// <summary>
    /// Reservar una cita. <see cref="Slot"/> admite <b>las dos formas</b>: el instante ISO que
    /// usaba el contrato original (<c>"2026-09-18T08:00:00Z"</c>) y el objeto
    /// <c>{ date, time }</c> que manda la UI, que es el que el propio borde DEVUELVE en
    /// <c>AppointmentDto.Slot</c>.
    /// </summary>
    /// <remarks>
    /// Con <c>DateTime?</c> a secas, el objeto de la UI hacía reventar a
    /// <c>System.Text.Json</c> y la petición salía <b>400 siempre</b>. No se veía porque el
    /// cliente de la UI envuelve el fallo y devuelve la cita optimista que ya tenía en
    /// pantalla: el paciente leía «cita agendada» y no había ninguna.
    /// </remarks>
    public sealed record BookAppointmentBody(string PatientId, string DoctorId, JsonElement? Slot)
    {
        /// <summary>El inicio del slot, venga como instante o como <c>{date,time}</c>.</summary>
        public DateTime? ResolveSlotUtc() => ReadSlot(Slot);
    }

    /// <summary>
    /// Signos vitales de la petición. Lleva <b>los dos juegos de nombres</b>: los del seam
    /// (<c>systolicMmHg</c>…) y los que emite y lee la UI (<c>systolic</c>…). Todos opcionales:
    /// lo que no llegue queda nulo, que es distinto de cero.
    /// </summary>
    public sealed record VitalsBody(
        double? SystolicMmHg = null, double? DiastolicMmHg = null, double? HeartRateBpm = null,
        double? TemperatureC = null, double? WeightKg = null, double? HeightCm = null,
        double? GlucoseMgDl = null, double? OxygenSaturationPct = null,
        double? Systolic = null, double? Diastolic = null, double? HeartRate = null,
        double? Temperature = null, double? Weight = null, double? Height = null,
        double? Glucose = null)
    {
        public ClinicalVitals ToClinicalVitals() => new(
            Systolic ?? SystolicMmHg, Diastolic ?? DiastolicMmHg,
            HeartRate ?? HeartRateBpm, Temperature ?? TemperatureC,
            Weight ?? WeightKg, Height ?? HeightCm,
            Glucose ?? GlucoseMgDl, OxygenSaturationPct);
    }

    /// <summary>
    /// La nota SOAP de la petición.
    /// </summary>
    /// <remarks>
    /// <para><b><c>objective</c> admite texto Y objeto</b>, porque el propio borde lo DEVUELVE
    /// como objeto: <c>SoapDto.Objective</c> es un <see cref="VitalsDto"/>. Tipado como
    /// <c>string?</c>, el cuerpo que la UI construye a partir de lo que este mismo controller le
    /// dio hacía reventar a <c>System.Text.Json</c> y <c>POST /encounter</c> salía <b>400
    /// siempre</b> — y el clínico veía su nota en pantalla igual, porque el cliente cae a la
    /// versión optimista. Una nota clínica que se da por guardada y no se guardó es el peor de
    /// los fallos silenciosos de esta superficie.</para>
    ///
    /// <para>Los signos vitales se leen de <c>objective</c> cuando viene como objeto y de
    /// <c>vitals</c> cuando viene aparte; la clave legacy se conserva.</para>
    /// </remarks>
    public sealed record SoapBody(
        string? Subjective,
        JsonElement? Objective,
        string? Assessment,
        string? Plan,
        VitalsBody? Vitals)
    {
        /// <summary>El texto libre del objetivo, si <c>objective</c> llegó como cadena.</summary>
        public string ObjectiveText()
            => Objective is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? string.Empty : string.Empty;

        /// <summary>Los signos vitales: de <c>objective</c> si es objeto, si no de <c>vitals</c>.</summary>
        public ClinicalVitals? ResolveVitals()
        {
            if (Objective is { ValueKind: JsonValueKind.Object } obj)
            {
                var fromObjective = obj.Deserialize<VitalsBody>(VitalsJson);
                if (fromObjective is not null)
                {
                    return fromObjective.ToClinicalVitals();
                }
            }
            return Vitals?.ToClinicalVitals();
        }
    }

    public sealed record AddEncounterBody(string PatientId, SoapBody Soap, string? DoctorId, string? ReasonForVisit, string? DiagnosisCode);

    /// <summary>
    /// Una línea de la receta. <c>drug</c>/<c>dose</c> son las claves que la UI manda —y que el
    /// borde ya devuelve en <see cref="PrescriptionItemDto"/>—; <c>medicationName</c>/<c>dosage</c>
    /// se conservan. Sin las primeras, la receta se guardaba <b>sin nombre de medicamento</b>:
    /// <c>System.Text.Json</c> descarta lo que no mapea y nadie se entera.
    /// </summary>
    public sealed record PrescriptionItemBody(
        string? MedicationName = null,
        string? Dosage = null,
        string? Frequency = null,
        int DurationDays = 0,
        string? Instructions = null,
        string? Drug = null,
        string? Dose = null)
    {
        public string ResolveDrug() => FirstNonBlank(Drug, MedicationName);
        public string ResolveDose() => FirstNonBlank(Dose, Dosage);
    }

    public sealed record AddPrescriptionBody(string PatientId, IReadOnlyList<PrescriptionItemBody> Items, string? DoctorId, string? EncounterId);

    // ── Response DTOs (JSON estable para la UI) ────────────────────────

    public sealed record PatientDto(
        string Id, string Name, string Document, string Sex,
        int Age, string Phone, string Email, string BloodType,
        IReadOnlyList<string> Problems, IReadOnlyList<string> Allergies,
        string PrimaryDoctorId, bool Active,
        string City, string? AvatarUrl);

    public sealed record PatientsResponse(IReadOnlyList<PatientDto> Patients);

    public sealed record DoctorDto(
        string Id, string Name, string Specialty, string License,
        string Phone, string Email, bool AcceptingPatients,
        double Rating, int YearsExperience, string? AvatarUrl,
        IReadOnlyList<int> WorkingDays, int SlotStartHour, int SlotEndHour, int SlotMinutes);

    public sealed record DoctorsResponse(IReadOnlyList<DoctorDto> Doctors);

    public sealed record VitalsDto(
        double Systolic, double Diastolic, double HeartRate, double Temperature,
        double Weight, double Height, double Glucose);

    public sealed record SoapDto(string Subjective, VitalsDto Objective, string Assessment, string Plan);

    public sealed record HistoryDto(
        string PatientId, string ChiefComplaint,
        IReadOnlyList<string> ActiveProblems, IReadOnlyList<string> PastMedicalHistory,
        IReadOnlyList<string> Allergies, IReadOnlyList<string> CurrentMedications,
        VitalsDto? BaselineVitals, DateTime? LastVisitUtc, int TotalEncounters);

    public sealed record EncounterDto(
        string Id, string PatientId, string DoctorId, string DoctorName,
        string Date, string Reason, SoapDto Soap, string Signature);

    public sealed record EncounterEnvelope(EncounterDto Encounter);

    public sealed record PrescriptionItemDto(string Drug, string Dose, string Frequency, int DurationDays);

    public sealed record PrescriptionDto(
        string Id, string PatientId, string DoctorId, string DoctorName,
        string Date, IReadOnlyList<PrescriptionItemDto> Items,
        IReadOnlyList<string> Interactions);

    public sealed record PrescriptionEnvelope(PrescriptionDto Prescription);

    public sealed record AppointmentSlotDto(string Date, string Time);

    public sealed record AppointmentDto(
        string Id, string PatientId, string PatientName, string DoctorId, string DoctorName,
        string Date, string Time, int DurationMin, string Reason, string Status,
        AppointmentSlotDto Slot, string ReservationId);

    public sealed record AppointmentsResponse(IReadOnlyList<AppointmentDto> Appointments);

    public sealed record AppointmentEnvelope(AppointmentDto Appointment);

    /// <summary>
    /// La historia clínica del paciente hacia la UI.
    /// </summary>
    /// <remarks>
    /// <b><see cref="History"/> es la lista de notas</b> —lo que lee <c>chart.history[]</c>— y
    /// <b><see cref="ClinicalHistory"/> es el resumen</b> (motivo, problemas activos, alergias,
    /// basales). Antes las dos cosas compartían la clave <c>history</c> y ganaba el resumen, así
    /// que la pestaña «Notas» del clínico llegaba vacía. El resumen no se quitó: se le dio nombre
    /// propio, que es lo único que permite emitir las dos.
    /// </remarks>
    public sealed record PatientChartResponse(
        PatientDto Patient,
        IReadOnlyList<EncounterDto> History,
        IReadOnlyList<EncounterDto> Encounters,
        IReadOnlyList<PrescriptionDto> Prescriptions,
        IReadOnlyList<AppointmentDto> Appointments,
        HistoryDto? ClinicalHistory = null);

    // ── OLA 7 · Request bodies ──────────────────────────────────────────

    /// <summary>
    /// Solicitud de resurtido. <c>patientId</c>/<c>medicationId</c> son las claves de la UI
    /// —y las que el borde devuelve en <see cref="RefillDto"/>—; <c>patient</c>/<c>medId</c>
    /// se conservan. Con sólo las segundas declaradas, el cuerpo de la UI llegaba <b>vacío</b>
    /// y el endpoint contestaba 400 <b>siempre</b>; el paciente veía «Solicitada» porque el
    /// cliente marca optimista antes de llamar.
    /// </summary>
    public sealed record RefillBody(
        string? Patient = null,
        string? MedId = null,
        string? Note = null,
        string? PatientId = null,
        string? MedicationId = null)
    {
        public string ResolvePatient() => FirstNonBlank(PatientId, Patient);
        public string ResolveMedication() => FirstNonBlank(MedicationId, MedId);
    }

    /// <summary>
    /// Enviar un mensaje al equipo de salud. <c>user</c> es quien escribe según la UI;
    /// <c>from</c> se conserva. Sin <c>user</c>, el remitente llegaba nulo y el endpoint
    /// contestaba 400 siempre.
    /// </summary>
    public sealed record SendMessageBody(
        string? From = null,
        string? To = null,
        string? Body = null,
        string? ThreadId = null,
        string? User = null)
    {
        public string ResolveFrom() => FirstNonBlank(User, From);
    }

    /// <summary>
    /// Colocar una orden clínica. <c>patientId</c> y <c>kind</c> son las claves de la UI;
    /// <c>patient</c>/<c>type</c> se conservan.
    /// </summary>
    /// <remarks>
    /// <b><c>provider</c> no viaja desde la UI y no se inventa</b>: se resuelve al médico
    /// tratante del paciente (<c>EhrPatient.PrimaryDoctorId</c>), que es un dato del padrón y
    /// no una constante de relleno. Si el paciente no tiene tratante, el endpoint lo dice
    /// —400— en vez de atribuirle la orden a nadie.
    /// </remarks>
    public sealed record PlaceOrderBody(
        string? Patient = null,
        string? Provider = null,
        string? Type = null,
        string? Detail = null,
        string? PatientId = null,
        string? Kind = null)
    {
        public string ResolvePatient() => FirstNonBlank(PatientId, Patient);
        public string ResolveType() => FirstNonBlank(Kind, Type);
    }

    // ── OLA 7 · Response DTOs ───────────────────────────────────────────

    public sealed record HomeCardDto(
        string Id, string Kind, string Title, string Detail,
        string? Action, string? ActionLabel, string Tone);

    public sealed record PortalHomeResponse(
        PatientDto Patient,
        IReadOnlyList<HomeCardDto> Cards,
        AppointmentDto? NextAppointment,
        long BalanceMinor,
        string Currency,
        int UnreadMessages,
        int PendingCheckins);

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
        string Id, string PatientId, string Panel, string Name,
        string Value, string Unit, double? RefLow, double? RefHigh,
        string Flag, string Date, bool Released, string Comment);

    public sealed record LabResultsResponse(IReadOnlyList<LabResultDto> Results);

    public sealed record MedicationDto(
        string Id, string PatientId, string Drug, string Dose, string Frequency,
        string Instructions, string Pharmacy, int RefillsLeft, string? RefillStatus);

    public sealed record MedicationsResponse(IReadOnlyList<MedicationDto> Medications);

    public sealed record RefillDto(
        string Id, string PatientId, string MedicationId, string MedicationName,
        string ProviderId, DateTime RequestedAtUtc, string Status, string? Note);

    public sealed record RefillEnvelope(string Status, RefillDto Refill);

    public sealed record OrderDto(
        string Id, string PatientId, string ProviderId, string ProviderName,
        string Type, string Detail, DateTime PlacedAtUtc, string Status);

    public sealed record OrderEnvelope(OrderDto Order);

    public sealed record InBasketItemDto(
        string Id, string Kind, string PatientId, string PatientName,
        string Title, string Detail, string Priority, DateTime CreatedAtUtc, bool Done);

    public sealed record InBasketResponse(IReadOnlyList<InBasketItemDto> Items);

    public sealed record ThreadMessageDto(
        string Id, string Author, string Body, DateTime CreatedAtUtc, bool Outgoing);

    public sealed record ThreadDto(
        string Id, string Participant, string Subject, string LastMessage,
        DateTime LastAtUtc, int Unread, IReadOnlyList<ThreadMessageDto> Messages);

    public sealed record ThreadEnvelope(ThreadDto Thread);

    public sealed record MessagesResponse(IReadOnlyList<ThreadDto> Threads);

    public sealed record BillingLineDto(
        string Id, string Date, string Description, long AmountMinor);

    public sealed record BillingStatementDto(
        string PatientId, string Currency, long BalanceMinor, bool PlanActive,
        IReadOnlyList<BillingLineDto> Lines);

    public sealed record BillingDto(BillingStatementDto Statement);
}
