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

    public DefaultPhiAccessGuard(
        IMemberAccessGate gate,
        IConsentLedger consent,
        IAuditTrailWriter audit,
        ILogger<DefaultPhiAccessGuard> logger)
    {
        _gate = gate;
        _consent = consent;
        _audit = audit;
        _logger = logger;
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

        // Auto-acceso: el paciente sobre su propio recurso.
        if (request.TargetOwnerMemberKey is Guid owner && owner == actorKey)
        {
            return (true, null);
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
