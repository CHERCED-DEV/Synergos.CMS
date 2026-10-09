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

    /// <summary>
    /// El NOMBRE del miembro —el de su ficha—, o nulo si no tiene uno. Nunca su correo.
    /// </summary>
    /// <remarks>
    /// <b>No es <c>Identity.Name</c></b>: en un miembro de Umbraco eso es el login, y el registro lo llena
    /// con el correo. Leído como nombre, el aviso de una compra decía «Hola ana@…» y la entrada salía con
    /// el correo impreso como portador (ADR 0140 F3). Se toma el nombre del miembro por su Id, igual que
    /// <see cref="CurrentMemberKey"/>; y si no hay, el login sólo cuando no es un correo.
    /// </remarks>
    public string? CurrentMemberDisplayName
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            if (http?.User?.Identity is not { IsAuthenticated: true } identidad) return null;

            if (Miembro(http)?.Name is { } nombre && !string.IsNullOrWhiteSpace(nombre) && !EsCorreo(nombre))
            {
                return nombre.Trim();
            }
            return string.IsNullOrWhiteSpace(identidad.Name) || EsCorreo(identidad.Name) ? null : identidad.Name;
        }
    }

    private static bool EsCorreo(string valor) => valor.Contains('@', StringComparison.Ordinal);

    /// <summary>El miembro de la sesión por el Id de su claim, o nulo.</summary>
    private static Umbraco.Cms.Core.Models.IMember? Miembro(HttpContext http)
    {
        var raw = http.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(raw, out var memberId)) return null;
        return (http.RequestServices?.GetService(typeof(IMemberService)) as IMemberService)?.GetById(memberId);
    }

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
