using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Filters;

/// <summary>
/// Exige que el Member de la sesión tenga <b>alguno</b> de los roles indicados. Se declara UNA
/// vez sobre la clase y protege TODAS sus actions, incluidas las que se agreguen mañana.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> La auditoría F2 midió que <c>AdminController</c> repetía el
/// mismo <c>if (!_gate.HasAnyRole(...)) return Forbid();</c> en sus 29 actions. Ninguna lo
/// olvidaba —se verificaron una por una—, así que esto <b>no cierra un agujero abierto: cierra
/// la forma de un agujero futuro</b>. Con el check a mano, una action nueva nace PÚBLICA y solo
/// la protege que alguien se acuerde de copiar cuatro líneas; con el filtro nace protegida, y
/// abrirla exige un acto deliberado (<see cref="AllowAnonymousAttribute"/>) que se ve en el
/// diff. Es el mismo razonamiento —y el mismo molde— de <see cref="DevSeedOnlyAttribute"/>:
/// <i>"un endpoint nuevo nace gateado por omisión, no porque alguien se acuerde"</i>.</para>
///
/// <para><b>Preserva el comportamiento, no lo cambia.</b> Emite
/// <see cref="ControllerBase.Forbid()"/>, que es exactamente lo que devuelven 28 de las 29
/// actions hoy — incluido para el anónimo, a quien el pipeline de Umbraco lleva al login. Este
/// refactor es estructural (declarativo en vez de repetido); mezclarlo con un cambio de
/// semántica 401/403 haría imposible verificar ninguno de los dos. Si algún día se quiere
/// distinguir "identifícate" de "no tienes permiso" —hoy ambos terminan en el login, que para
/// un autenticado sin rol es un bucle— es un cambio aparte, anotado en el backlog.</para>
///
/// <para><b>La única action cuyo comportamiento SÍ cambia</b> es <c>AuditExportCsv</c>: devuelve
/// <c>Task</c> (escribe el CSV directo en <c>Response.Body</c>), así que no podía usar
/// <c>Forbid()</c> y hacía <c>Response.StatusCode = 403</c> a mano. Ahora recibe el mismo trato
/// que las otras 28. Es una mejora, no una regresión: ese endpoint se alcanza como enlace de
/// descarga desde la página de auditoría, y si la sesión expiró, mandar al login es más útil
/// que un 403 crudo en una pestaña en blanco.</para>
///
/// <para><b>Corta ANTES de la acción</b> (<see cref="IActionFilter.OnActionExecuting"/>): ningún
/// dato se lee ni se escribe. Es también lo que permite proteger de forma uniforme actions que
/// devuelven tipos distintos (<c>IActionResult</c>, <c>Task</c>, <c>Task&lt;IActionResult&gt;</c>).</para>
///
/// <para><b>Lo que este filtro NO hace:</b> antiforgery. Ese eslabón lo cubre
/// <c>[AutoValidateAntiforgeryToken]</c> sobre <c>AdminController</c>, que al ser filtro de
/// autorización corre ANTES que éste: un POST sin token nunca llega a evaluarse por rol.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireRolesAttribute : TypeFilterAttribute
{
    /// <param name="rolesCsv">
    /// Roles permitidos, separados por coma (ej. <c>"admin,moderator,editor"</c>). Basta con
    /// tener UNO. Es CSV y no <c>params string[]</c> porque
    /// <see cref="IMemberAccessGate.HasAnyRole"/> ya habla ese formato y todos los guards del
    /// repo lo usan así.
    /// </param>
    public RequireRolesAttribute(string rolesCsv)
        : base(typeof(RequireRolesFilter))
    {
        Arguments = new object[] { rolesCsv };
    }

    private sealed class RequireRolesFilter : IActionFilter
    {
        private readonly IMemberAccessGate _gate;
        private readonly string _rolesCsv;

        public RequireRolesFilter(IMemberAccessGate gate, string rolesCsv)
        {
            _gate = gate;
            _rolesCsv = rolesCsv;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            // Una action puede optar por salirse explícitamente. Se mira SOLO el método, nunca
            // la clase: AdminController lleva [AllowAnonymous] a nivel de CLASE para saltarse el
            // pipeline de auth de Umbraco y gestionar el permiso él mismo. Si este filtro mirara
            // los metadatos del endpoint (que agregan clase + método), encontraría ese
            // [AllowAnonymous] heredado y se desactivaría a sí mismo en las 29 actions —
            // convirtiendo el refactor en un agujero abierto de par en par.
            if (context.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor descriptor
                && descriptor.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), inherit: false))
            {
                return;
            }

            if (!_gate.HasAnyRole(_rolesCsv))
            {
                // Idéntico a lo que devolvían las 28 actions: el pipeline decide si eso es un
                // redirect al login o un 403, y este refactor no cambia esa decisión.
                context.Result = new ForbidResult();
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
