using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
/// <para><b>Distingue "identifícate" de "no te alcanza".</b> Son dos respuestas distintas a dos
/// preguntas distintas, y colapsarlas deja al visitante sin salida:</para>
/// <list type="bullet">
///   <item><description><b>Anónimo</b> → <see cref="ChallengeResult"/>. El esquema de cookies lo
///   manda al login con el <c>returnUrl</c> puesto, que es lo que un dashboard de navegador
///   necesita (un 401 crudo en una pestaña no le sirve a nadie).</description></item>
///   <item><description><b>Autenticado sin el rol</b> → <b>403</b> real, renderizando
///   <c>Views/Shared/AccessDenied.cshtml</c>, con la URL intacta. Mandarlo al login sería
///   pedirle que repita lo único que ya hizo bien.</description></item>
/// </list>
///
/// <para><b>Lo que había antes.</b> Ambos casos emitían <c>Forbid()</c>, y eso terminaba en un
/// 302 a <c>/Account/AccessDenied</c> — una ruta que <b>no existe</b> en este proyecto. Se
/// verificó contra la app: caía al "No published content" de Umbraco y devolvía <b>200 OK</b>.
/// O sea: ni login para el anónimo, ni explicación para el autenticado, y un 200 sobre lo que
/// era una denegación. El refactor de auth declarativa preservó ese comportamiento a propósito
/// —no mezclar estructura con semántica— y lo dejó anotado; esto es ese cambio aparte.</para>
///
/// <para><b><c>AuditExportCsv</c></b> devuelve <c>Task</c> (escribe el CSV directo en
/// <c>Response.Body</c>), así que nunca pudo usar <c>Forbid()</c> y hacía
/// <c>Response.StatusCode = 403</c> a mano. Con el filtro recibe el mismo trato que las otras
/// 28, y ahora ese 403 vuelve a ser un 403 — solo que con una página que lo explica en vez de
/// una pestaña en blanco.</para>
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

    /// <summary>
    /// Vista que se renderiza —con 403— cuando el visitante SÍ está autenticado pero le falta el
    /// rol. Vive en <c>Views/Shared/</c> porque el motor de vistas cae ahí para cualquier
    /// controller, así que el filtro se puede poner sobre cualquiera sin arrastrar una vista.
    /// </summary>
    internal const string AccessDeniedViewName = "AccessDenied";

    private sealed class RequireRolesFilter : IActionFilter
    {
        private readonly IMemberAccessGate _gate;
        private readonly IModelMetadataProvider _modelMetadata;
        private readonly ITempDataDictionaryFactory _tempDataFactory;
        private readonly string _rolesCsv;

        public RequireRolesFilter(
            IMemberAccessGate gate,
            IModelMetadataProvider modelMetadata,
            ITempDataDictionaryFactory tempDataFactory,
            string rolesCsv)
        {
            _gate = gate;
            _modelMetadata = modelMetadata;
            _tempDataFactory = tempDataFactory;
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

            if (!_gate.IsAuthenticated)
            {
                // "No sé quién sos" — 401 en semántica. En un dashboard que se navega con el
                // navegador, lo útil no es un 401 crudo sino el challenge del esquema de
                // cookies, que lleva al login con el returnUrl puesto.
                context.Result = new ChallengeResult();
                return;
            }

            if (!_gate.HasAnyRole(_rolesCsv))
            {
                // "Sé quién sos y no te alcanza" — 403 de verdad, con la URL intacta y una
                // página que lo explica. Mandarlo al login sería pedirle que haga otra vez lo
                // único que ya hizo bien.
                context.Result = new ViewResult
                {
                    ViewName = AccessDeniedViewName,
                    StatusCode = StatusCodes.Status403Forbidden,
                    ViewData = new ViewDataDictionary(_modelMetadata, context.ModelState),
                    TempData = _tempDataFactory.GetTempData(context.HttpContext),
                };
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
