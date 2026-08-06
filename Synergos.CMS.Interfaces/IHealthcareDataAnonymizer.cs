namespace Synergos.CMS.Interfaces;

/// <summary>
/// De-identifica los registros clínicos de un miembro ANTES del hard-delete
/// RTBF (ADR 0098), para no dejar PHI huérfana ni violar la retención legal:
/// blanquea identificadores (nombre, fecha exacta → solo año), desliga el
/// <c>MemberKey</c>, y CONSERVA el contenido clínico de-identificado (debe
/// retenerse aunque el Member se borre).
/// </summary>
/// <remarks>
/// Lo invoca <c>IGdprRtbfCoordinator</c> antes de borrar el Member.
/// Implementación: <c>Synergos.CMS.Web.Services.FileSystemHealthcareDataAnonymizer</c>
/// sobre <see cref="IPhiStore"/>. Idempotente (un segundo pase no encuentra match,
/// porque ya desligó el MemberKey).
/// </remarks>
public interface IHealthcareDataAnonymizer
{
    /// <summary>
    /// De-identifica los registros de paciente cuyo <c>MemberKey</c> es
    /// <paramref name="memberKey"/>. Devuelve cuántos de-identificó.
    /// </summary>
    Task<int> AnonymizeForMemberAsync(Guid memberKey, CancellationToken cancellationToken);
}
