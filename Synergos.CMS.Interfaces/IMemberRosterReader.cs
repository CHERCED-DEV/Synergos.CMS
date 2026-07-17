namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam read-only para listar Members registrados — usado por la
/// vista admin <c>/admin/members</c>. Aísla el dashboard del
/// <c>IMemberService</c> de Umbraco para mantener la regla del grafo
/// (Application no referencia <c>Umbraco.Cms.*</c> — ADR 0002).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.UmbracoMemberRosterReader</c> y
/// consume <see cref="IMemberService"/> + <see cref="IMemberRoleService"/>
/// internamente.
///
/// Read-only por diseño — el seam NO expone Create/Update/Delete porque
/// (a) el editor Members ya existe en backoffice, (b) las acciones
/// destructivas merecen su propio seam con ADR aparte cuando lleguen.
/// </remarks>
public interface IMemberRosterReader
{
    /// <summary>
    /// Devuelve una página de Members ordenados por <c>CreateDate</c>
    /// descendente (más recientes primero). <paramref name="roleFilter"/>
    /// opcional limita a Members con ese role asignado (case-insensitive).
    /// </summary>
    MemberRosterPage GetRosterPage(int page, int pageSize, string? roleFilter = null);

    /// <summary>
    /// Lista todos los roles registrados, útil para popular el dropdown
    /// de filter en la vista. Ordenados alfabéticamente.
    /// </summary>
    IReadOnlyList<string> ListAllRoles();
}

/// <summary>
/// Snapshot de un Member para listing — sin datos sensibles (password
/// hash, security stamps, etc.).
/// </summary>
/// <param name="Key">Identificador GUID estable del Member.</param>
/// <param name="Email">Email principal — usado como login en flujos
///   de Members runtime.</param>
/// <param name="DisplayName">Nombre legible. Fallback al Email si el
///   Member no setó display name.</param>
/// <param name="Roles">Roles asignados. Vacío si el Member no tiene
///   roles (Members "sin rol" simplemente no son moderators ni
///   editors).</param>
/// <param name="LastLoginUtc">Último login conocido. Null si nunca se
///   ha logueado o si el campo no se persistió.</param>
/// <param name="CreatedUtc">Timestamp de creación del Member.</param>
/// <param name="IsApproved">Si el Member ya confirmó su email (cuando
///   email confirmation está activa).</param>
/// <param name="IsLockedOut">Si el Member está locked-out por intentos
///   fallidos o lockout admin.</param>
public sealed record MemberRosterItem(
    Guid Key,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    DateTime? LastLoginUtc,
    DateTime CreatedUtc,
    bool IsApproved,
    bool IsLockedOut);

/// <summary>
/// Página paginada de Members.
/// </summary>
public sealed record MemberRosterPage(
    IReadOnlyList<MemberRosterItem> Items,
    int Page,
    int PageSize,
    long TotalCount,
    string? RoleFilter)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)((TotalCount + PageSize - 1) / PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
