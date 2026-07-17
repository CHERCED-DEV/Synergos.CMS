using System.Security.Claims;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

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

    public string? CurrentMemberEmail =>
        _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;

    public Guid? CurrentMemberKey
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            var raw = http?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            // Si el claim YA es un GUID (setups que lo emiten así), úsalo directo.
            if (Guid.TryParse(raw, out var direct))
            {
                return direct;
            }

            // Umbraco 13: el claim NameIdentifier del cookie de member lleva el
            // Member.Id (ENTERO), no el Key (GUID) — y el GUID no viaja en ningún
            // otro claim. Lo resolvemos del Id vía IMemberService (cache de Umbraco).
            // Se toma del request scope: este gate es Singleton e IMemberService es
            // Scoped (resolverlo por ctor sería un captive dependency).
            if (!int.TryParse(raw, out var memberId) || http is null)
            {
                return null;
            }

            var members = http.RequestServices.GetService(typeof(IMemberService)) as IMemberService;
            return members?.GetById(memberId)?.Key;
        }
    }

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
