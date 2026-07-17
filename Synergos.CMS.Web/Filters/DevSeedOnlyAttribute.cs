using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Filters;

/// <summary>
/// Marca una superficie HTTP como <strong>dev-only</strong>: existe solo con
/// <c>Synergos:DevSeed:Enabled=true</c> y responde 404 cuando el flag está off
/// (ADR 0013). Molde de <c>DevController</c>, pero aplicado a nivel de CLASE.
/// </summary>
/// <remarks>
/// Se declara UNA vez sobre el controller en vez de repetir el check en cada
/// acción: un endpoint nuevo nace gateado por omisión, no por que alguien se
/// acuerde. Aplica a controllers cuya data sale de un seed de demo — sirven
/// datos fabricados y no tienen por qué existir en un deploy real.
///
/// 404 y no 403 a propósito: la respuesta honesta para una superficie que no
/// se despliega es "no existe".
///
/// Alcance real, verificado en vivo: con el flag off, TODO GET y todo POST con
/// body válido dan 404 sin ejecutar la acción ni tocar data. Un POST con body
/// INVÁLIDO da 400, no 404 — la validación de modelo de <c>[ApiController]</c>
/// corre antes que los action filters. O sea: este atributo garantiza que no
/// se sirva ni se escriba nada, NO que la existencia del endpoint quede oculta.
/// Si algún día hace falta lo segundo, requiere un middleware por ruta (antes
/// del model binding), no un action filter.
/// </remarks>
public sealed class DevSeedOnlyAttribute : TypeFilterAttribute
{
    public DevSeedOnlyAttribute() : base(typeof(DevSeedOnlyFilter))
    {
    }

    private sealed class DevSeedOnlyFilter : IActionFilter
    {
        private readonly DevSeedSettings _settings;

        public DevSeedOnlyFilter(IOptions<DevSeedSettings> settings)
        {
            _settings = settings.Value;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!_settings.Enabled)
            {
                // Corta ANTES de la acción: no se toca ningún dato.
                context.Result = new NotFoundResult();
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
