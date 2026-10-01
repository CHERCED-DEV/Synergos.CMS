using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Synergos.CMS.Web.Filters;

/// <summary>
/// Declara que la superficie hace un trabajo que EXCEDE el plazo de petición
/// (<see cref="Middlewares.TimeoutMiddleware"/>) y que hay que dejarlo terminar para poder
/// contestar qué hizo.
/// </summary>
/// <remarks>
/// <para>Nació del sembrador (#188): <c>POST /dev/fill-synergos-pages</c> tarda unos dos minutos
/// en síncrono contra un plazo de treinta segundos. El plazo no lo paraba —el trabajo no mira el
/// token— y sólo conseguía borrar la respuesta: <c>200</c> con el cuerpo vacío, medido tres veces,
/// y el <c>FillResult</c> sólo en el log.</para>
///
/// <para>Apaga el plazo con el rasgo estándar de ASP.NET Core
/// (<see cref="IHttpRequestTimeoutFeature.DisableTimeout"/>), que el middleware publica: no hay
/// una lista de rutas exentas que alguien tenga que mantener al lado. Corre como filtro de
/// RECURSO, el primero de la tubería de MVC, para apagarlo antes de que empiece el trabajo.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class SinPlazoDePeticionAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
        => context.HttpContext.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
