using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPhiAccessGuard"/> (ADR 0098). Decide acceso a PHI por
/// rol + pertenencia + consentimiento, y AUDITA cada intento. Fail-closed: si
/// la auditoría no se puede escribir, deniega aunque la política permitiera.
/// </summary>
/// <remarks>
/// Política (estricta por defecto): el paciente accede a lo suyo
/// (TargetOwnerMemberKey == actor); el staff (doctor/nurse/reception) agenda
/// citas; los datos clínicos (historia/recetas/consentimiento) requieren rol
/// doctor CON consentimiento vigente del paciente. Cualquier otro caso: denegado.
/// </remarks>
public sealed class DefaultPhiAccessGuard : IPhiAccessGuard
{
    private const string DoctorRole = "doctor";
    private const string SchedulingRolesCsv = "doctor,nurse,reception";

    private readonly IMemberAccessGate _gate;
    private readonly IConsentLedger _consent;
    private readonly IAuditTrailWriter _audit;
    private readonly ILogger<DefaultPhiAccessGuard> _logger;
    private readonly IPatientRepository? _patients;

    /// <param name="patients">Opcional. Resuelve el Member DUEÑO de un paciente
    /// para la rama de auto-acceso. Nullable a propósito: sin él el guard sigue
    /// funcionando exactamente como antes —decidiendo sólo por rol y
    /// consentimiento— y los tests que lo construyen a mano no cambian.</param>
    public DefaultPhiAccessGuard(
        IMemberAccessGate gate,
        IConsentLedger consent,
        IAuditTrailWriter audit,
        ILogger<DefaultPhiAccessGuard> logger,
        IPatientRepository? patients = null)
    {
        _gate = gate;
        _consent = consent;
        _audit = audit;
        _logger = logger;
        _patients = patients;
    }

    public async Task<AccessDecision> CheckAccessAsync(AccessCheckRequest request, CancellationToken cancellationToken)
    {
        bool allowed;
        string? reason;
        try
        {
            (allowed, reason) = await EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Un fallo al evaluar (p.ej. IO del consent ledger) NO debe saltarse
            // la auditoría: se deniega y se audita igual.
            _logger.LogError(ex,
                "DefaultPhiAccessGuard: error evaluando política — acceso DENEGADO a {Resource}",
                request.ResourceType);
            allowed = false;
            reason = "evaluation-error";
        }

        // Auditar SIEMPRE (permiso o negación). Fail-closed: si el audit falla,
        // se deniega aunque la política permitiera.
        var evt = new AuditEvent(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            _gate.CurrentMemberEmail ?? string.Empty,
            _gate.CurrentMemberDisplayName ?? string.Empty,
            allowed ? "phi.access-granted" : "phi.access-denied",
            $"{request.ResourceType}:{request.TargetPatientKey?.ToString("N") ?? "-"}",
            allowed ? "success" : "failure",
            $"action={request.Action}; reason={reason ?? "ok"}");

        try
        {
            await _audit.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DefaultPhiAccessGuard: fallo de auditoría — acceso DENEGADO (fail-closed) a {Resource}",
                request.ResourceType);
            return new AccessDecision(false, "audit-unavailable", evt.Id);
        }

        return new AccessDecision(allowed, allowed ? null : reason, evt.Id);
    }

    private async Task<(bool allowed, string? reason)> EvaluateAsync(AccessCheckRequest request, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid actorKey)
        {
            return (false, "not-authenticated");
        }

        // ── Auto-acceso: el paciente sobre su propio recurso, y SÓLO PARA LEER.
        //
        // Este bloque estaba MUERTO. El único llamante (HealthcareApiController)
        // construía el AccessCheckRequest con tres argumentos posicionales, así
        // que TargetOwnerMemberKey era null siempre: ningún paciente podía leer
        // su propio expediente, sus citas ni sus recetas — todo caía a la rama
        // de roles y devolvía 403. El portal del paciente que ADR 0098 describe
        // no existía. El test que cubría esta rama la probaba en aislamiento,
        // nunca a través del controller.
        //
        // Ahora el dueño lo resuelve el GUARD, no el controller, por dos
        // razones: (1) quien decide debe reunir lo que necesita para decidir, y
        // (2) va DESPUÉS del chequeo de sesión de arriba, así que un anónimo no
        // provoca ni una lectura del repositorio — la propiedad "no se toca PHI
        // antes de autorizar" que verifica GetPatient_Anonymous_Returns401.
        //
        // El "read" es deliberado y NO estaba en el diseño original: sin él, un
        // paciente podría reescribir su propia historia clínica — las notas de
        // hallazgos y de valoración son campos de PatientRecord. Un paciente
        // tiene derecho a VER lo suyo; el criterio clínico lo firma quien lo
        // emite. Como la rama nunca se alcanzaba, estrecharla no le quita
        // permiso a nadie que hoy lo tenga.
        if (string.Equals(request.Action, "read", StringComparison.OrdinalIgnoreCase))
        {
            var owner = request.TargetOwnerMemberKey;

            // PatientKey ≠ MemberKey (IPatientRepository.cs:34-35). Si el
            // llamante no nos dio el dueño, lo resolvemos por la clave clínica.
            if (owner is null && request.TargetPatientKey is Guid patientKey && _patients is not null)
            {
                var patient = await _patients.GetAsync(patientKey, ct);
                owner = patient?.MemberKey;
            }

            if (owner is Guid ownerKey && ownerKey == actorKey)
            {
                return (true, null);
            }
        }

        // Agenda: operación de staff (no requiere consentimiento clínico).
        if (string.Equals(request.ResourceType, "appointment", StringComparison.OrdinalIgnoreCase)
            && _gate.HasAnyRole(SchedulingRolesCsv))
        {
            return (true, null);
        }

        // Datos clínicos (historia/recetas/consentimiento): doctor CON consentimiento.
        if (_gate.HasAnyRole(DoctorRole))
        {
            if (request.TargetPatientKey is not Guid patientKey)
            {
                return (false, "no-target-patient");
            }
            var hasConsent = await _consent.HasActiveConsentAsync(patientKey, actorKey, ct).ConfigureAwait(false);
            return hasConsent ? (true, null) : (false, "no-active-consent");
        }

        return (false, "insufficient-role");
    }
}
