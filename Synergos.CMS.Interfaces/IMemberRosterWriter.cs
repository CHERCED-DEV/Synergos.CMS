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

    /// <summary>
    /// Hard-delete del Member. <b>Irreversible</b> — no hay undo por
    /// diseño (a diferencia de comments / form submissions). El caller
    /// debe confirmar con el operador via dialog antes de invocar.
    /// Comments / forms previously authored persist con author orphan
    /// (no se cascadan a delete). Olas 181-184.
    /// </summary>
    /// <returns>True si el Member fue borrado; false si no existía.</returns>
    Task<bool> DeleteAsync(Guid memberKey, CancellationToken cancellationToken);

    /// <summary>
    /// Genera un password-reset token + envía el email correspondiente
    /// al Member. El email contiene un link que el Member abre para
    /// reestablecer su password. La generación del token NO modifica
    /// el password actual — Member sigue pudiendo loguear con el
    /// existente hasta que use el link. Olas 181-184.
    /// </summary>
    /// <returns>True si se envió email; false si el Member no existe
    ///   o no tiene email.</returns>
    Task<bool> SendPasswordResetAsync(Guid memberKey, CancellationToken cancellationToken);

    /// <summary>
    /// Reemplaza el set de roles del Member con los <paramref name="roleNames"/>
    /// indicados. Roles que estaban antes y no están ahora se quitan;
    /// roles nuevos se agregan. Idempotent — pasar el mismo set es
    /// no-op. Olas 181-184.
    /// </summary>
    /// <returns>True si se aplicó el cambio (incluso si fue no-op);
    ///   false si el Member no existe.</returns>
    Task<bool> SetRolesAsync(
        Guid memberKey,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken);
}
