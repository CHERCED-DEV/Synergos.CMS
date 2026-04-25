namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de autenticación de miembros. Abstrae la verificación de
/// identidad y roles del miembro actual sin acoplar consumidores a
/// <c>IMemberManager</c> de Umbraco.
/// </summary>
/// <remarks>
/// Implementación por defecto en
/// <c>Synergos.CMS.Web.Services.DefaultMemberAccessGate</c> usando
/// <c>IMemberManager</c>. Consumidores: PublishedRequest notification
/// handler que aplica <c>compMemberGating.requiresAuth</c> +
/// <c>allowedRolesCsv</c>, blocks <c>elementMemberGate</c> +
/// <c>elementMemberLogin</c> + <c>elementMemberLogout</c>.
///
/// Decisión de diseño: el seam responde "puede el miembro actual ver
/// esta página" — no toma la acción (redirect, 401). La acción la
/// decide el handler que lo consume.
/// </remarks>
public interface IMemberAccessGate
{
    /// <summary>
    /// True si hay un miembro autenticado en el request actual.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Nombre visible del miembro autenticado, o null si anónimo.
    /// </summary>
    string? CurrentMemberDisplayName { get; }

    /// <summary>
    /// Roles del miembro autenticado. Vacío si anónimo.
    /// </summary>
    IReadOnlyCollection<string> CurrentMemberRoles { get; }

    /// <summary>
    /// True si el miembro autenticado tiene al menos uno de los roles
    /// indicados (CSV). Vacío o null = solo requiere autenticación,
    /// cualquier rol vale.
    /// </summary>
    /// <param name="allowedRolesCsv">Roles permitidos separados por
    ///   coma (ej. "premium,staff"). Case-insensitive.</param>
    bool HasAnyRole(string? allowedRolesCsv);
}
