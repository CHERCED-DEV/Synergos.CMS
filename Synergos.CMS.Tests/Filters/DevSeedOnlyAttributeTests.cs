using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Filters;

namespace Synergos.CMS.Tests.Filters;

/// <summary>
/// Cubre <see cref="DevSeedOnlyAttribute"/> — la PARED que hace aceptable que un dashboard
/// demo entero (<see cref="EhrController"/>, censo clínico fabricado, endpoints anónimos a
/// propósito) exista en el código de producción.
/// </summary>
/// <remarks>
/// <para><b>Por qué esta pared merece tests más que el controller que protege.</b> La
/// auditoría F2 encontró la asimetría: EhrController tiene 1.024 LOC y cero tests, pero su
/// seguridad NO depende de su código — depende de que este atributo responda 404 con el flag
/// apagado. Si el filtro se rompe (un rename del setting, un cambio de DI, un refactor del
/// options binding), el censo demo queda público en producción <i>con build verde</i>. Esa
/// clase de regresión silenciosa es exactamente lo que un test debe clavar.</para>
///
/// <para><b>La segunda mitad es que la pared esté PUESTA.</b> Un filtro perfecto no protege un
/// controller que ya no lo declara, así que aquí también se fija que EhrController lleva el
/// atributo — quitarlo pasa de "diff de una línea que nadie nota" a "test rojo que exige
/// justificación".</para>
///
/// <para>El filtro real es una clase privada anidada (decisión correcta: nadie debe
/// instanciarlo a mano), así que se alcanza por reflexión. Si el nombre interno cambia, el
/// test falla con un mensaje claro en vez de compilar contra otra cosa.</para>
/// </remarks>
public sealed class DevSeedOnlyAttributeTests
{
    private static IActionFilter CreateFilter(bool enabled)
    {
        var filterType = typeof(DevSeedOnlyAttribute)
            .GetNestedType("DevSeedOnlyFilter", BindingFlags.NonPublic);
        Assert.NotNull(filterType); // si renombran el filtro interno, que se sepa aquí
        return (IActionFilter)Activator.CreateInstance(
            filterType!,
            Options.Create(new DevSeedSettings { Enabled = enabled }))!;
    }

    private static ActionExecutingContext Context()
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    [Fact]
    public void Con_el_flag_apagado_responde_404_ANTES_de_ejecutar_la_accion()
    {
        var context = Context();

        CreateFilter(enabled: false).OnActionExecuting(context);

        // 404 y no 403 es parte del contrato (ADR 0013): la respuesta honesta para una
        // superficie que no se despliega es "no existe". Un 403 confirmaría que existe.
        Assert.IsType<NotFoundResult>(context.Result);
    }

    [Fact]
    public void Con_el_flag_encendido_deja_pasar_sin_tocar_el_resultado()
    {
        var context = Context();

        CreateFilter(enabled: true).OnActionExecuting(context);

        // Result == null significa "sigue el pipeline": la acción se ejecuta normal.
        Assert.Null(context.Result);
    }

    [Fact]
    public void El_default_del_setting_es_APAGADO_o_sea_fail_closed()
    {
        // Si alguien cambia el default de DevSeedSettings a true, todos los deploys sin
        // configuración explícita publicarían las superficies demo. El default ES la política.
        Assert.False(new DevSeedSettings().Enabled);
    }

    [Fact]
    public void EhrController_LLEVA_la_pared_puesta()
    {
        // El filtro perfecto no protege un controller que dejó de declararlo. Este assert
        // convierte "quitar [DevSeedOnly]" en un test rojo en vez de un diff invisible.
        var attribute = typeof(EhrController)
            .GetCustomAttributes(typeof(DevSeedOnlyAttribute), inherit: false);

        Assert.NotEmpty(attribute);
    }
}
