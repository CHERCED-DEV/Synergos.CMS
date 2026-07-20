using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>Lo que quedó tras sembrar: qué grupos y qué member recibió qué roles.</summary>
public sealed record DevRoleSeedResult(
    IReadOnlyList<string> RolesCreated,
    IReadOnlyList<string> RolesAlreadyPresent,
    string? MemberEmail,
    IReadOnlyList<string> RolesAssigned,
    string? Note);

/// <summary>
/// Tooling DEV-ONLY que crea los member groups de dominio y se los asigna a un member
/// existente (ADR 0013: cero seeding en boot, todo tras <c>Synergos:DevSeed:Enabled</c>).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Cuatro olas de seguridad (T2-Gobierno, T9, T2-Eventos, T7)
/// dejaron consolas correctamente CERRADAS por rol —<c>funcionario</c>, <c>organizador</c>,
/// <c>doctor</c>— pero <b>ninguna se podía demostrar</b>: los grupos no existían y crearlos
/// exigía backoffice. El trabajo estaba hecho y era invisible. Esto lo vuelve visible con
/// un POST.</para>
/// <para><b>Lo que NO hace, a propósito:</b> no crea members ni toca contraseñas. Asigna
/// roles a un member que YA existe (el que se le pase, o el único que haya). Crear
/// identidades y credenciales es del arquitecto; repartir permisos de demo es tooling.</para>
/// <para>Idempotente: un rol que ya existe no se duplica, y un member que ya lo tiene no
/// se altera. Correrlo dos veces da el mismo estado.</para>
/// </remarks>
public sealed class DevMemberRoleSeeder
{
    /// <summary>
    /// Los roles de dominio que los guards del proyecto consultan hoy. Están aquí —y no
    /// en config— porque son el CONTRATO con el código: <c>GovController</c> pide
    /// "funcionario,admin", <c>EventosController</c> "organizador,admin" y
    /// <c>DefaultPhiAccessGuard</c> "doctor,nurse,reception". Cambiar uno aquí sin
    /// cambiarlo allá no arreglaría nada.
    /// </summary>
    public static readonly IReadOnlyList<string> DomainRoles = new[]
    {
        "funcionario",  // Gobierno — cola/expediente/decisión (T2-Gobierno)
        "organizador",  // Eventos — consola, crear evento, check-in, feed en vivo (T2-Eventos, T7)
        "agente",       // Propiedades — leads (PII) + publicar al catálogo (T2-Realty)
        "doctor",       // Healthcare — PHI (ADR 0098)
        "nurse",
        "reception",
        "admin",        // superusuario transversal
    };

    private readonly IMemberService _memberService;
    private readonly ILogger<DevMemberRoleSeeder> _logger;

    public DevMemberRoleSeeder(IMemberService memberService, ILogger<DevMemberRoleSeeder> logger)
    {
        _memberService = memberService;
        _logger = logger;
    }

    /// <summary>
    /// Crea los member groups que falten y, si se indica un <paramref name="memberEmail"/>
    /// (o hay exactamente uno registrado), le asigna <paramref name="rolesToAssign"/>.
    /// </summary>
    public DevRoleSeedResult Seed(string? memberEmail, IReadOnlyList<string>? rolesToAssign)
    {
        // Ojo con la asimetría de la API de Umbraco 13: GetAllRoles() devuelve los
        // GRUPOS (IMemberGroup) y GetAllRoles(memberId) devuelve los NOMBRES (string).
        var existing = (_memberService.GetAllRoles() ?? Enumerable.Empty<Umbraco.Cms.Core.Models.IMemberGroup>())
            .Select(g => g.Name ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<string>();
        var already = new List<string>();
        foreach (var role in DomainRoles)
        {
            if (existing.Contains(role))
            {
                already.Add(role);
                continue;
            }
            _memberService.AddRole(role);
            created.Add(role);
        }

        if (created.Count > 0)
        {
            _logger.LogInformation("DevSeed: member groups creados: {Roles}", string.Join(", ", created));
        }

        // ── Asignación (opcional) ────────────────────────────────────────────────
        var member = ResolveMember(memberEmail, out var note);
        if (member is null)
        {
            return new DevRoleSeedResult(created, already, null, Array.Empty<string>(), note);
        }

        var wanted = (rolesToAssign is { Count: > 0 } ? rolesToAssign : DomainRoles)
            .Where(r => DomainRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var current = (_memberService.GetAllRoles(member.Id) ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = wanted.Where(r => !current.Contains(r)).ToArray();
        if (toAdd.Length > 0)
        {
            _memberService.AssignRoles(new[] { member.Id }, toAdd);
            _logger.LogInformation(
                "DevSeed: roles {Roles} asignados a {Email}", string.Join(", ", toAdd), member.Email);
        }

        // Se devuelve el estado FINAL leído del servicio, no lo que se pidió: si la
        // asignación no cuajó, el reporte no debe decir que sí.
        var final = (_memberService.GetAllRoles(member.Id) ?? Array.Empty<string>()).ToArray();
        return new DevRoleSeedResult(created, already, member.Email, final, note);
    }

    /// <summary>
    /// Resuelve a quién asignarle los roles. Sin email explícito, solo se asigna si hay
    /// UN member registrado: con varios, elegir por el agente sería adivinar a quién dar
    /// permisos de funcionario.
    /// </summary>
    private Umbraco.Cms.Core.Models.IMember? ResolveMember(string? memberEmail, out string? note)
    {
        note = null;

        if (!string.IsNullOrWhiteSpace(memberEmail))
        {
            var byEmail = _memberService.GetByEmail(memberEmail.Trim());
            if (byEmail is null)
            {
                note = $"No existe un member con el correo '{memberEmail}'. Los grupos SÍ quedaron creados; " +
                       "regístrelo desde /account/register y vuelva a llamar con ese correo.";
            }
            return byEmail;
        }

        var all = _memberService.GetAllMembers()?.ToList() ?? new List<Umbraco.Cms.Core.Models.IMember>();
        if (all.Count == 1)
        {
            return all[0];
        }

        note = all.Count == 0
            ? "No hay members registrados. Los grupos quedaron creados; registre uno en /account/register " +
              "y vuelva a llamar con ?email= para asignarle los roles."
            : $"Hay {all.Count} members: indique ?email= a cuál asignarle los roles (no se elige por usted). " +
              $"Candidatos: {string.Join(", ", all.Select(m => m.Email))}.";
        return null;
    }
}
