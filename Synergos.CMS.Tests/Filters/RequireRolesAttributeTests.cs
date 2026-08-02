using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Filters;

namespace Synergos.CMS.Tests.Filters;

/// <summary>
/// Cubre <see cref="RequireRolesAttribute"/> — la autorización declarativa que reemplazó los 29
/// checks repetidos a mano de <see cref="AdminController"/> (auditoría F2, backlog Alto #1).
/// </summary>
/// <remarks>
/// <para><b>Qué protege, exactamente.</b> No un agujero abierto: se verificó que las 29 actions
/// tenían su check y ninguna lo olvidaba. Lo que protege es la FORMA del agujero futuro — que
/// el default pase de abierto (una action nueva nace pública si nadie copia el guard) a cerrado
/// (nace protegida). Estos tests son los que hacen que ese default siga siendo cerrado mañana.</para>
///
/// <para><b>El caso que casi se cuela.</b> <c>AdminController</c> lleva
/// <c>[AllowAnonymous]</c> a nivel de CLASE, para saltarse el pipeline de auth de Umbraco y
/// resolver el permiso él mismo. Un filtro que consultara
/// <c>ActionDescriptor.EndpointMetadata</c> —que agrega clase + método— encontraría ese
/// <c>[AllowAnonymous]</c> heredado y <b>se desactivaría a sí mismo en las 29 actions</b>,
/// convirtiendo el refactor en un agujero de par en par y con build verde. El test
/// <c>El_AllowAnonymous_de_la_CLASE_no_desactiva_el_filtro</c> es el que clava eso.</para>
/// </remarks>
public sealed class RequireRolesAttributeTests
{
    private const string Roles = "admin,moderator,editor";

    /// <summary>Doble de controller que reproduce la forma real: AllowAnonymous en la CLASE.</summary>
    [AllowAnonymous]
    [RequireRoles(Roles)]
    private sealed class ProtectedController : Controller
    {
        public IActionResult Protegida() => Ok();

        [AllowAnonymous]
        public IActionResult Publica() => Ok();
    }

    private static IActionFilter CreateFilter(IMemberAccessGate gate)
    {
        var filterType = typeof(RequireRolesAttribute)
            .GetNestedType("RequireRolesFilter", BindingFlags.NonPublic);
        Assert.NotNull(filterType); // si renombran el filtro interno, que falle aquí y no en silencio
        return (IActionFilter)Activator.CreateInstance(filterType!, gate, Roles)!;
    }

    private static ActionExecutingContext Context(string methodName)
    {
        var method = typeof(ProtectedController).GetMethod(methodName)!;
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = method,
            ControllerTypeInfo = typeof(ProtectedController).GetTypeInfo(),
            // EndpointMetadata agrega clase + método, igual que en runtime: aquí vive el
            // [AllowAnonymous] de la clase que el filtro NO debe honrar.
            EndpointMetadata = typeof(ProtectedController)
                .GetCustomAttributes(inherit: true)
                .Concat(method.GetCustomAttributes(inherit: true))
                .ToList(),
        };

        return new ActionExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private static IMemberAccessGate Gate(bool authenticated, string? rolesHeld)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(authenticated);
        gate.HasAnyRole(Arg.Any<string>()).Returns(call =>
        {
            var allowed = call.Arg<string>();
            if (!authenticated || rolesHeld is null || allowed is null) return false;
            var held = rolesHeld.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return allowed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(r => held.Contains(r, StringComparer.OrdinalIgnoreCase));
        });
        return gate;
    }

    [Fact]
    public void El_AllowAnonymous_de_la_CLASE_no_desactiva_el_filtro()
    {
        // AdminController lleva [AllowAnonymous] en la clase por una razón legítima (saltarse
        // el pipeline de Umbraco). Si el filtro lo honrara, las 29 actions quedarían abiertas.
        var context = Context(nameof(ProtectedController.Protegida));

        CreateFilter(Gate(authenticated: false, rolesHeld: null)).OnActionExecuting(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Un_autenticado_SIN_el_rol_es_denegado()
    {
        var context = Context(nameof(ProtectedController.Protegida));

        CreateFilter(Gate(authenticated: true, rolesHeld: "member")).OnActionExecuting(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("moderator")]
    [InlineData("editor")]
    [InlineData("member,editor")]
    public void Basta_con_tener_UNO_de_los_roles(string rolesHeld)
    {
        var context = Context(nameof(ProtectedController.Protegida));

        CreateFilter(Gate(authenticated: true, rolesHeld: rolesHeld)).OnActionExecuting(context);

        // Result == null significa "sigue el pipeline": la acción se ejecuta.
        Assert.Null(context.Result);
    }

    [Fact]
    public void El_AllowAnonymous_del_METODO_si_abre_esa_action()
    {
        // La escotilla explícita: marcar UNA action como pública sin sacarla del controller.
        // Es deliberado y se ve en el diff — al revés que olvidar cuatro líneas.
        var context = Context(nameof(ProtectedController.Publica));

        CreateFilter(Gate(authenticated: false, rolesHeld: null)).OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    // ── Que la pared esté PUESTA donde importa ────────────────────────────────

    [Fact]
    public void AdminController_LLEVA_el_atributo_con_los_roles_correctos()
    {
        // Un filtro perfecto no protege un controller que dejó de declararlo. Esto convierte
        // "quitar [RequireRoles]" en un test rojo en vez de un diff de una línea que nadie nota.
        var attribute = typeof(AdminController)
            .GetCustomAttributes(typeof(RequireRolesAttribute), inherit: false)
            .Cast<RequireRolesAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(new object[] { "admin,moderator,editor" }, attribute!.Arguments);
    }

    [Fact]
    public void NINGUNA_action_de_AdminController_quedo_con_el_check_a_mano()
    {
        // Si alguien vuelve a meter un guard manual, es señal de que no confía en el filtro —
        // y dos mecanismos de auth conviviendo es cómo se cuelan las inconsistencias que la
        // auditoría encontró en ShopCatalogController.
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Controllers", "AdminController.cs"));

        Assert.DoesNotContain("HasAnyRole(ModeratorRolesCsv)", source, StringComparison.Ordinal);
    }

    /// <summary>Sube desde el bin/ del test hasta la raíz del repo (donde está el .sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
