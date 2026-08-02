using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMemberRosterReader"/> implementado sobre
/// <see cref="IMemberService"/> + <see cref="IMemberGroupService"/>
/// de Umbraco.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de tipos
/// <c>Umbraco.Cms.Core.Services</c> (no se filtran a Application por
/// ADR 0002).
///
/// Pagination via <c>IMemberService.GetAll(...)</c> — el service maneja el offset
/// internamente. Roles via <see cref="IMemberService.GetAllRoles(int)"/> por member.
/// </remarks>
public sealed class UmbracoMemberRosterReader : IMemberRosterReader
{
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;

    public UmbracoMemberRosterReader(
        IMemberService memberService,
        IMemberGroupService memberGroupService)
    {
        _memberService = memberService;
        _memberGroupService = memberGroupService;
    }

    public MemberRosterPage GetRosterPage(int page, int pageSize, string? roleFilter = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        var pageIndex = page - 1;

        // memberTypeAlias NULL = "todos los tipos". Con string.Empty, Umbraco arma el predicado
        // ContentTypeAlias == "" y el roster salía SIEMPRE vacío — la página /admin/members
        // mostraba "Members · 0" con members registrados, dejando lock / unlock / reset 2FA /
        // password-reset / roles / delete / GDPR-erase sin forma de alcanzarse desde la UI.
        // Se usa la sobrecarga larga (la de orderBySystemField) porque es la única que declara
        // el parámetro nullable; la corta pide string y obligaba a un null!.
        var allMembers = _memberService.GetAll(
            pageIndex,
            pageSize,
            out var totalRecords,
            orderBy: "CreateDate",
            orderDirection: Umbraco.Cms.Core.Direction.Descending,
            orderBySystemField: true,
            memberTypeAlias: null,
            filter: string.Empty);

        var items = new List<MemberRosterItem>();
        foreach (var m in allMembers)
        {
            var roles = _memberService.GetAllRoles(m.Id) ?? Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(roleFilter) &&
                !roles.Any(r => string.Equals(r, roleFilter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            DateTime? lastLogin = m.LastLoginDate.HasValue
                ? m.LastLoginDate.Value.ToUniversalTime()
                : null;

            items.Add(new MemberRosterItem(
                Key: m.Key,
                Email: m.Email ?? string.Empty,
                DisplayName: string.IsNullOrWhiteSpace(m.Name) ? (m.Email ?? string.Empty) : m.Name,
                Roles: roles.ToList().AsReadOnly(),
                LastLoginUtc: lastLogin,
                CreatedUtc: m.CreateDate.ToUniversalTime(),
                IsApproved: m.IsApproved,
                IsLockedOut: m.IsLockedOut));
        }

        return new MemberRosterPage(
            Items: items,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalRecords,
            RoleFilter: roleFilter);
    }

    public IReadOnlyList<string> ListAllRoles()
    {
        var groups = _memberGroupService.GetAll() ?? Array.Empty<Umbraco.Cms.Core.Models.IMemberGroup>();
        return groups
            .Select(g => g.Name ?? string.Empty)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
