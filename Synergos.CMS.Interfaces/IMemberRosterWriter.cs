namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam write para acciones admin sobre el Member roster — split del
/// <see cref="IMemberRosterReader"/> por ISP. Mantiene el dashboard
/// admin limpio del editor backoffice de Umbraco (que sigue siendo
/// el canon para CRUD completo).
/// </summary>
/// <remarks>
/// Solo expone acciones <b>booleanas / reversibles</b> (lock/unlock).
/// Cambios destructivos (delete, reset password, role-toggle) merecen
/// su propio seam con threat model aparte cuando lleguen.
///
/// Implementación por defecto en
/// <c>Synergos.CMS.Web.Services.UmbracoMemberRosterWriter</c>.
/// </remarks>
public interface IMemberRosterWriter
{
    /// <summary>
    /// Marca al Member como locked-out. El Member NO puede loguear
    /// hasta que se ejecute <see cref="UnlockAsync"/>. Idempotent —
    /// volver a llamar sobre un Member ya locked es no-op.
    /// </summary>
    /// <returns>True si la operación tuvo efecto (Member encontrado +
    ///   estado cambió o ya estaba locked); false si el Member no
    ///   existe.</returns>
    Task<bool> LockAsync(Guid memberKey, CancellationToken cancellationToken);

    /// <summary>
    /// Quita el lockout. Idempotent — sobre un Member no-locked es
    /// no-op.
    /// </summary>
    /// <returns>True si la operación tuvo efecto; false si el Member
    ///   no existe.</returns>
    Task<bool> UnlockAsync(Guid memberKey, CancellationToken cancellationToken);
}
