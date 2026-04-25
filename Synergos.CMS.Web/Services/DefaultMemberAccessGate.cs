using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMemberAccessGate"/>. Lee el principal del
/// <see cref="HttpContext"/> actual — funciona con Umbraco Members
/// (Identity claims) sin acoplar a <c>IMemberManager</c>.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IHttpContextAccessor"/>. Operaciones sync —
/// <see cref="HttpContext.User"/> es la fuente de verdad y ya está
/// poblada por el middleware de auth de Umbraco antes de llegar a
/// templates Razor o notification handlers.
///
/// Roles se leen de claims con tipo <see cref="ClaimTypes.Role"/>.
/// Comparación case-insensitive contra el CSV configurado en el
/// schema (compMemberGating.allowedRolesCsv).
/// </remarks>
public sealed class DefaultMemberAccessGate : IMemberAccessGate
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DefaultMemberAccessGate(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public string? CurrentMemberDisplayName =>
        _httpContextAccessor.HttpContext?.User?.Identity?.Name;

    public IReadOnlyCollection<string> CurrentMemberRoles
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null)
            {
                return Array.Empty<string>();
            }

            return user.Claims
                .Where(c => string.Equals(c.Type, ClaimTypes.Role, StringComparison.Ordinal))
                .Select(c => c.Value)
                .ToArray();
        }
    }

    public bool HasAnyRole(string? allowedRolesCsv)
    {
        if (string.IsNullOrWhiteSpace(allowedRolesCsv))
        {
            return IsAuthenticated;
        }

        if (!IsAuthenticated)
        {
            return false;
        }

        var allowed = allowedRolesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowed.Length == 0)
        {
            return true;
        }

        var roles = CurrentMemberRoles;
        foreach (var role in allowed)
        {
            if (roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
