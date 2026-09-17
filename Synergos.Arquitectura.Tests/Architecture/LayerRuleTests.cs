using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El grafo de capas del ADR 0002 como INVARIANTE ejecutable, no como costumbre:
/// <c>Interfaces ← Application ← Web ← Tests</c>, y las dos capas de abajo no conocen
/// ni Umbraco ni ASP.NET.
/// </summary>
/// <remarks>
/// <para><b>Por qué un test y no una revisión.</b> La Fase 0 de la auditoría de arquitectura
/// (docs/product/01-plan-auditoria-arquitectura.md) verificó que la regla se cumple hoy —
/// cero referencias, cero usings prohibidos. Pero nada la vigilaba: un
/// <c>&lt;PackageReference&gt;</c> agregado por conveniencia y un <c>using Umbraco.*</c>
/// compilarían sin que nadie lo note, y el desmonte costaría N call-sites en vez de un
/// rechazo de PR. Es la misma jugada que el gate de reconstrucción (ADR 0128): primero se
/// midió que la propiedad se cumple, después se le puso alambrada.</para>
///
/// <para><b>Cómo funciona.</b> Se inspeccionan las referencias REALES de los ensamblados
/// compilados (<c>GetReferencedAssemblies</c>), no los csproj: el compilador solo referencia
/// lo que el código usa de verdad, así que cualquier uso genuino de un tipo prohibido aparece
/// aquí. Un paquete agregado pero sin usar no aparece — y tampoco importa todavía: este test
/// atrapa el momento en que el código lo TOCA, que es cuando nace el acople.</para>
/// </remarks>
public sealed class LayerRuleTests
{
    // Tipos ancla: cualquier tipo de cada ensamblado sirve; estos existen desde esta ola.
    private static readonly System.Reflection.Assembly Interfaces =
        typeof(ISeatMapProvider).Assembly;

    private static readonly System.Reflection.Assembly Application =
        typeof(StubCabinSeatMapProvider).Assembly;

    /// <summary>Prefijos que las capas puras tienen PROHIBIDO conocer (ADR 0002).</summary>
    private static readonly string[] Forbidden =
    {
        "Umbraco",
        "Microsoft.AspNetCore",
        "uSync",
    };

    private static IEnumerable<string> ForbiddenRefs(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(name => Forbidden.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n, StringComparer.Ordinal);

    [Fact]
    public void Interfaces_no_conoce_Umbraco_ni_AspNetCore()
    {
        // Interfaces es la capa más profunda: seams puros. Si esto falla, el seam dejó de ser
        // seam — la "abstracción" ya está casada con el framework que debía aislar.
        Assert.Empty(ForbiddenRefs(Interfaces));
    }

    [Fact]
    public void Application_no_conoce_Umbraco_ni_AspNetCore()
    {
        // Application es lógica pura + stubs. Es la capa que hace baratos los 118 archivos de
        // tests de services: si se acopla a Umbraco, cada test nuevo paga el peaje de mockear
        // un CMS entero (la razón por la que los controllers casi no tienen tests).
        Assert.Empty(ForbiddenRefs(Application));
    }

    [Fact]
    public void Application_referencia_Interfaces_y_nada_mas_del_repo()
    {
        var propias = Application.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(n => n.StartsWith("Synergos", StringComparison.Ordinal))
            .ToList();

        // La flecha va en UNA dirección: Application → Interfaces. Una referencia a Web sería
        // circular (el compilador la impide), pero este assert también documenta la intención
        // para el día en que aparezca un cuarto proyecto "Común" que tiente a todos.
        Assert.Equal(new[] { "Synergos.CMS.Interfaces" }, propias);
    }

    [Fact]
    public void Interfaces_no_referencia_ningun_otro_proyecto_del_repo()
    {
        var propias = Interfaces.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(n => n.StartsWith("Synergos", StringComparison.Ordinal));

        Assert.Empty(propias);
    }
}
