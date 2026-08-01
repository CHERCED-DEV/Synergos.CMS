using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del vertical Healthcare (ADR 0098). La consume la app clínica
/// Angular <c>&lt;synergos-healthcare&gt;</c>. <strong>RECORD-KEEPER</strong>: registra
/// historia/agenda/recetas/consentimiento — NO diagnostica ni aconseja (sin
/// endpoints con verbos clínicos: suggest/diagnose/recommend).
/// </summary>
/// <remarks>
/// <see cref="IPhiAccessGuard"/> se invoca PRIMERO en cada endpoint (audita +
/// decide). Fail-closed: si el guard niega, no se toca ningún dato. 401 si no
/// autenticado, 403 si autenticado sin permiso.
/// </remarks>
[ApiController]
[Route("api/healthcare")]
public sealed class HealthcareApiController : ControllerBase
{
    private readonly IPhiAccessGuard _guard;
    private readonly IPatientRepository _patients;
    private readonly IAppointmentScheduler _appointments;
    private readonly IPrescriptionService _prescriptions;
    private readonly IConsentLedger _consent;
    private readonly IMemberAccessGate _gate;

    public HealthcareApiController(
        IPhiAccessGuard guard,
        IPatientRepository patients,
        IAppointmentScheduler appointments,
        IPrescriptionService prescriptions,
        IConsentLedger consent,
        IMemberAccessGate gate)
    {
        _guard = guard;
        _patients = patients;
        _appointments = appointments;
        _prescriptions = prescriptions;
        _consent = consent;
        _gate = gate;
    }

    /// <summary>
    /// El expediente del paciente que está pidiendo. Es la puerta del portal del paciente.
    /// </summary>
    /// <remarks>
    /// <b>Sin este endpoint el portal no podía existir</b>, y no por falta de permiso: el
    /// auto-acceso del guard ya deja que un paciente lea lo suyo, pero cada endpoint se
    /// direcciona por <c>patientKey</c>, que es una clave clínica DISTINTA del
    /// <c>MemberKey</c> —así lo exige el RTBF— y que nadie le dice al paciente. El permiso
    /// estaba concedido y era inalcanzable: había que adivinar un GUID.
    ///
    /// <para><b>El orden de las tres operaciones es la parte de seguridad.</b>
    /// (1) Se corta al anónimo ANTES de tocar el repositorio, que es la propiedad que
    /// <c>GetPatient_Anonymous_Returns401</c> ya fija para el resto del controller.
    /// (2) Se resuelve la clave a partir del miembro de la sesión —nunca de un parámetro—,
    /// así que no hay forma de preguntar por el expediente de otro: no existe entrada donde
    /// escribir un ajeno.
    /// (3) Y aun así se pasa por el guard, que es quien AUDITA. Saltárselo porque "es su
    /// propio expediente" dejaría el único acceso sin rastro, justo el que más lo necesita.</para>
    ///
    /// <para>Un miembro sin expediente recibe 404 y no un cuerpo vacío: es la respuesta
    /// honesta, y no distingue "no tienes" de "no existe" para nadie más, porque solo se
    /// puede preguntar por uno mismo.</para>
    /// </remarks>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }

        // Solo la CLAVE, nunca el expediente: es lo que hace segura esta lectura antes del
        // guard. Sin ella no se puede nombrar el recurso sobre el que se pide permiso.
        var patientKey = await _patients.FindKeyByMemberAsync(memberKey, ct);
        if (patientKey is null)
        {
            return NotFound();
        }

        if (await DenyAsync("patient-record", "read", patientKey, ct) is { } denied)
        {
            return denied;
        }

        var record = await _patients.GetAsync(patientKey.Value, ct);
        return record is null ? NotFound() : Ok(record);
    }

    // ── Pacientes ──────────────────────────────────────────────
    [HttpGet("patients")]
    public async Task<IActionResult> ListPatients([FromQuery] Guid? doctorKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", null, ct) is { } denied) return denied;
        return Ok(await _patients.ListAsync(new PatientQuery(DoctorKey: doctorKey), ct));
    }

    [HttpGet("patients/{patientKey:guid}")]
    public async Task<IActionResult> GetPatient(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct) is { } denied) return denied;
        var patient = await _patients.GetAsync(patientKey, ct);
        return patient is null ? NotFound() : Ok(patient);
    }

    [HttpPost("patients")]
    public async Task<IActionResult> UpsertPatient([FromBody] PatientRecord record, CancellationToken ct)
    {
        if (record is null) return BadRequest();
        if (await DenyAsync("patient-record", "write", record.PatientKey, ct) is { } denied) return denied;
        var key = await _patients.UpsertAsync(record, ct);
        return Ok(new { patientKey = key });
    }

    // ── Citas ──────────────────────────────────────────────────
    [HttpGet("appointments")]
    public async Task<IActionResult> ListAppointments([FromQuery] Guid? doctorKey, [FromQuery] Guid? patientKey, CancellationToken ct)
    {
        if (await DenyAsync("appointment", "read", patientKey, ct) is { } denied) return denied;
        return Ok(await _appointments.ListAsync(new AppointmentQuery(DoctorKey: doctorKey, PatientKey: patientKey), ct));
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> BookAppointment([FromBody] AppointmentRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest();
        if (await DenyAsync("appointment", "write", request.PatientKey, ct) is { } denied) return denied;
        var result = await _appointments.BookAsync(request, ct);
        return result.Booked ? Ok(result.Slot) : Conflict(new { reason = result.ConflictReason });
    }

    [HttpPost("appointments/{appointmentId:guid}/cancel")]
    public async Task<IActionResult> CancelAppointment(Guid appointmentId, [FromQuery] string? reason, CancellationToken ct)
    {
        if (await DenyAsync("appointment", "write", null, ct) is { } denied) return denied;
        return await _appointments.CancelAsync(appointmentId, reason ?? string.Empty, ct) ? Ok() : NotFound();
    }

    // ── Recetas ────────────────────────────────────────────────
    [HttpGet("patients/{patientKey:guid}/prescriptions")]
    public async Task<IActionResult> ListPrescriptions(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("prescription", "read", patientKey, ct) is { } denied) return denied;
        return Ok(await _prescriptions.ListForPatientAsync(patientKey, ct));
    }

    [HttpPost("prescriptions")]
    public async Task<IActionResult> IssuePrescription([FromBody] IssuePrescriptionRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest();
        if (await DenyAsync("prescription", "write", request.PatientKey, ct) is { } denied) return denied;
        return Ok(await _prescriptions.IssueAsync(request, ct));
    }

    // ── Consentimiento ─────────────────────────────────────────
    [HttpGet("patients/{patientKey:guid}/consent")]
    public async Task<IActionResult> ConsentHistory(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("consent", "read", patientKey, ct) is { } denied) return denied;
        return Ok(await _consent.GetConsentHistoryAsync(patientKey, ct));
    }

    [HttpPost("consent")]
    public async Task<IActionResult> GrantConsent([FromBody] ConsentGrant grant, CancellationToken ct)
    {
        if (grant is null) return BadRequest();
        if (await DenyAsync("consent", "write", grant.PatientKey, ct) is { } denied) return denied;
        var consentId = await _consent.GrantAsync(grant, ct);
        return Ok(new { consentId });
    }

    [HttpPost("consent/{consentId:guid}/revoke")]
    public async Task<IActionResult> RevokeConsent(Guid consentId, CancellationToken ct)
    {
        if (await DenyAsync("consent", "write", null, ct) is { } denied) return denied;
        await _consent.RevokeAsync(consentId, ct);
        return Ok();
    }

    // Guard-first: audita + decide. Devuelve el IActionResult de rechazo (401
    // si no autenticado, 403 si denegado) o null si pasa.
    private async Task<IActionResult?> DenyAsync(string resourceType, string action, Guid? targetPatientKey, CancellationToken ct)
    {
        // El dueño NO se resuelve acá. Este controller mantiene la propiedad
        // "guard primero, sin tocar PHI antes de autorizar" — que el test
        // GetPatient_Anonymous_Returns401 verifica exigiendo que el repositorio
        // no reciba ni una llamada cuando el llamante es anónimo.
        //
        // Quien decide es el guard, así que es el guard el que resuelve el
        // Member dueño del paciente, y sólo DESPUÉS de comprobar que hay sesión
        // (DefaultPhiAccessGuard.EvaluateAsync). Un anónimo sigue sin provocar
        // una sola lectura.
        var decision = await _guard.CheckAccessAsync(
            new AccessCheckRequest(resourceType, action, targetPatientKey), ct);
        if (decision.IsAllowed) return null;
        return string.Equals(decision.DenyReason, "not-authenticated", StringComparison.Ordinal)
            ? Unauthorized()
            : StatusCode(StatusCodes.Status403Forbidden);
    }
}
