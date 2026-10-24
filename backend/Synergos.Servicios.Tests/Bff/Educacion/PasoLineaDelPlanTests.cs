using Synergos.Bff.Core.Flow;
using Synergos.Bff.Educacion.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// El paso interino <c>educacion.linea</c> (010-educacion-matricula T003, NM-1): la línea a cotizar sale del curso y
/// del plan con el MISMO nombre de sujeto que publica la oferta.
/// </summary>
public sealed class PasoLineaDelPlanTests
{
    private static readonly Ref Curso = Ref.Create(SujetoDelPlan.ClaseDelCurso, "gestion-agil");

    private static Task<SalidaDePaso> Correr(Ref curso, string? plan)
        => new PasoLineaDelPlan().EjecutarAsync(
            new EntradaDePaso("s-1", Ref.Create("prueba.flujo", "s-1"),
                new PasoDef("linea", "educacion.linea", ["curso", "plan"], [], null, null, null, null, null),
                new FlowContext().Set("curso", curso).Set("plan", plan), null, null),
            CancellationToken.None);

    private static (Ref Sujeto, int Cantidad) Linea(SalidaDePaso salida)
        => Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(Assert.Single(salida.Valores)));

    [Fact]
    public async Task Sin_plan_cotiza_el_precio_del_curso()
    {
        Assert.Equal((Ref.Create("educacion.curso", "gestion-agil"), 1), Linea(await Correr(Curso, null)));
    }

    [Fact]
    public async Task Con_plan_cotiza_el_precio_de_ese_plan_de_ese_curso()
    {
        Assert.Equal((Ref.Create("educacion.plan", "gestion-agil/premium"), 1), Linea(await Correr(Curso, "premium")));
    }

    [Fact]
    public async Task Un_plan_en_blanco_es_el_precio_del_curso()
    {
        Assert.Equal((Ref.Create("educacion.curso", "gestion-agil"), 1), Linea(await Correr(Curso, "  ")));
    }

    [Theory]
    [InlineData("otro/curso", "full")]
    [InlineData("gestion-agil", "full/../otro")]
    [InlineData("../salud.profesional:doc", null)]
    public async Task Una_clave_con_otra_forma_se_rechaza_sin_llegar_a_precios(string curso, string? plan)
    {
        var salida = await Correr(Ref.Create(SujetoDelPlan.ClaseDelCurso, curso), plan);

        Assert.Equal(PasoLineaDelPlan.EntradaInvalida, salida.Rechazo?.Code);
        Assert.Empty(salida.Valores);
    }

    [Fact]
    public void La_oferta_y_la_linea_nombran_igual_el_precio_de_un_plan()
    {
        Assert.Equal(SujetoDelPlan.De(Curso, "full"), SujetoDelPlan.De(Ref.Create("educacion.curso", " gestion-agil "), " full "));
    }
}
