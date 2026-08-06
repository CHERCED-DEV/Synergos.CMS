namespace Synergos.CMS.Interfaces;

/// <summary>
/// Libro de consentimientos paciente→doctor (ADR 0098). Append-only: otorgar
/// y revocar agregan/marcan registros, nunca borran (rastro auditable). El
/// <see cref="IPhiAccessGuard"/> lo consulta para decidir si un doctor puede
/// acceder a la PHI de un paciente.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemConsentLedger</c> y persiste sobre
/// <see cref="IPhiStore"/> (resourceType <c>"consents"</c>).
/// </remarks>
public interface IConsentLedger
{
    /// <summary>Registra un consentimiento y devuelve su id.</summary>
    Task<Guid> GrantAsync(ConsentGrant grant, CancellationToken cancellationToken);

    /// <summary>Marca un consentimiento como revocado (no lo borra). No-op si no existe.</summary>
    Task RevokeAsync(Guid consentId, CancellationToken cancellationToken);

    /// <summary>
    /// True si existe un consentimiento vigente del paciente al doctor: no
    /// revocado y dentro de la ventana [GrantedAtUtc, ExpiresAtUtc].
    /// </summary>
    Task<bool> HasActiveConsentAsync(Guid patientKey, Guid doctorKey, CancellationToken cancellationToken);

    /// <summary>Historial de consentimientos de un paciente (más reciente primero).</summary>
    Task<IReadOnlyList<ConsentRecord>> GetConsentHistoryAsync(Guid patientKey, CancellationToken cancellationToken);
}

/// <summary>Solicitud de otorgamiento de consentimiento.</summary>
/// <param name="PatientKey">Paciente que otorga (clave clínica, ≠ MemberKey).</param>
/// <param name="DoctorKey">Doctor (MemberKey) al que se le otorga acceso.</param>
/// <param name="GrantedAtUtc">Inicio de vigencia (UTC).</param>
/// <param name="ExpiresAtUtc">Fin de vigencia (UTC).</param>
/// <param name="Purpose">Propósito: <c>"consultation"</c>, <c>"prescription"</c>,
///   <c>"records-access"</c>, etc.</param>
public sealed record ConsentGrant(
    Guid PatientKey,
    Guid DoctorKey,
    DateTime GrantedAtUtc,
    DateTime ExpiresAtUtc,
    string Purpose);

/// <summary>Registro inmutable de un consentimiento (con estado de revocación).</summary>
public sealed record ConsentRecord(
    Guid ConsentId,
    Guid PatientKey,
    Guid DoctorKey,
    DateTime GrantedAtUtc,
    DateTime ExpiresAtUtc,
    string Purpose,
    bool IsRevoked,
    DateTime? RevokedAtUtc);
