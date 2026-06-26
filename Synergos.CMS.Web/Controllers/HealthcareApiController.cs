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

    public HealthcareApiController(
        IPhiAccessGuard guard,
        IPatientRepository patients,
        IAppointmentScheduler appointments,
        IPrescriptionService prescriptions,
        IConsentLedger consent)
    {
        _guard = guard;
        _patients = patients;
        _appointments = appointments;
        _prescriptions = prescriptions;
        _consent = consent;
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
        var decision = await _guard.CheckAccessAsync(
            new AccessCheckRequest(resourceType, action, targetPatientKey), ct);
        if (decision.IsAllowed) return null;
        return string.Equals(decision.DenyReason, "not-authenticated", StringComparison.Ordinal)
            ? Unauthorized()
            : StatusCode(StatusCodes.Status403Forbidden);
    }
}
