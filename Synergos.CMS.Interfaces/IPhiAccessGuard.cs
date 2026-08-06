namespace Synergos.CMS.Interfaces;

/// <summary>
/// Punto ÚNICO de decisión de acceso a PHI (ADR 0098). Se invoca PRIMERO en
/// cada endpoint de Healthcare, antes de tocar cualquier dato. Combina rol +
/// pertenencia (paciente sobre lo suyo) + consentimiento (doctor→paciente), y
/// audita TODO intento de acceso.
/// </summary>
/// <remarks>
/// <strong>Fail-closed</strong>: la auditoría del acceso es obligatoria. Si la
/// escritura de auditoría falla, el acceso se DENIEGA (no-audit ⇒ no-access).
/// Implementación por defecto: <c>Synergos.CMS.Web.Services.DefaultPhiAccessGuard</c>.
/// </remarks>
public interface IPhiAccessGuard
{
    /// <summary>
    /// Decide y AUDITA un intento de acceso. Nunca lanza por política — un
    /// fallo de auditoría se traduce en <see cref="AccessDecision.IsAllowed"/>=false.
    /// </summary>
    Task<AccessDecision> CheckAccessAsync(AccessCheckRequest request, CancellationToken cancellationToken);
}

/// <summary>Descripción del acceso a evaluar.</summary>
/// <param name="ResourceType">Tipo de recurso: <c>"patient-record"</c>,
///   <c>"appointment"</c>, <c>"prescription"</c>, <c>"consent"</c>.</param>
/// <param name="Action">Acción: <c>"read"</c>, <c>"write"</c>, <c>"delete"</c>.</param>
/// <param name="TargetPatientKey">Paciente cuyo dato se accede (si aplica).</param>
/// <param name="TargetOwnerMemberKey">MemberKey dueño del recurso, cuando el
///   caller lo conoce — habilita el auto-acceso del paciente a lo suyo.</param>
public sealed record AccessCheckRequest(
    string ResourceType,
    string Action,
    Guid? TargetPatientKey = null,
    Guid? TargetOwnerMemberKey = null);

/// <summary>Resultado de la decisión, con el id del evento de auditoría emitido.</summary>
/// <param name="IsAllowed">Si el acceso se permite.</param>
/// <param name="DenyReason">Motivo de la negación (null si se permitió).</param>
/// <param name="AuditEventId">Id del evento de auditoría que se escribió.</param>
public sealed record AccessDecision(bool IsAllowed, string? DenyReason, string AuditEventId);
