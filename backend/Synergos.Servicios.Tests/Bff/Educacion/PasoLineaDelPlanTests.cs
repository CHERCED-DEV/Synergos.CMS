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

    private static Task<SalidaDePaso> Correr(Ref curso, string? plan, RastroDeCursos? rastro = null)
        => new PasoLineaDelPlan(rastro).EjecutarAsync(
            new EntradaDePaso("s-1", Ref.Create("prueba.flujo", "s-1"),
                new PasoDef("linea", "educacion.linea", ["curso", "plan"], [], null, null, null, null, null),
                new FlowContext().Set("curso", curso).Set("plan", plan), null, null),
            CancellationToken.None);

    private static (Ref Sujeto, int Cantidad) Linea(SalidaDePaso salida)
        => Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(salida.Valores[0]));

    /// <summary>Lo que escribe para el aviso: el título del curso y el nombre del plan (las dos últimas escrituras).</summary>
    private static (string? Curso, string? Plan) Nombres(SalidaDePaso salida)
    {
        Assert.Equal(3, salida.Valores.Count);
        return ((string?)salida.Valores[1], (string?)salida.Valores[2]);
    }

    /// <summary>Un rastro de ofertas en una carpeta propia, con lo que publicó el CMS.</summary>
    private static RastroDeCursos Rastro(params CursoOfertado[] ofertas)
    {
        var rastro = new RastroDeCursos(Path.Combine(Path.GetTempPath(), "rastro-" + Guid.NewGuid().ToString("N")));
        foreach (var oferta in ofertas) rastro.Guardar(oferta);
        return rastro;
    }

    private static CursoOfertado Gestion(string? nombre = "Gestión ágil", string? nombreDelPlan = "Pago de contado")
        => new("gestion-agil", 320_000m, "COP", [new PlanOfertado("full", 320_000m, nombreDelPlan)], Retirada: false, nombre);

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

    // ── Los nombres para el aviso (NM-2 de 010 con T091 del núcleo) ──────────────────────────

    [Fact]
    public async Task Escribe_el_titulo_del_curso_y_el_nombre_del_plan_que_publico_la_oferta()
    {
        Assert.Equal(("Gestión ágil", "Pago de contado"), Nombres(await Correr(Curso, "full", Rastro(Gestion()))));
    }

    [Fact]
    public async Task Sin_nombres_en_la_oferta_cae_a_la_clave_del_curso_y_al_codigo_del_plan()
    {
        Assert.Equal(("gestion-agil", "full"), Nombres(await Correr(Curso, "full", Rastro(Gestion(nombre: null, nombreDelPlan: null)))));
        Assert.Equal(("Gestión ágil", "premium"), Nombres(await Correr(Curso, "premium", Rastro(Gestion()))));
        Assert.Equal(("gestion-agil", "full"), Nombres(await Correr(Curso, "full")));
    }

    [Fact]
    public async Task Sin_plan_el_plan_no_tiene_nombre()
    {
        Assert.Equal(("Gestión ágil", null), Nombres(await Correr(Curso, null, Rastro(Gestion()))));
    }

    [Fact]
    public void La_oferta_y_la_linea_nombran_igual_el_precio_de_un_plan()
    {
        Assert.Equal(SujetoDelPlan.De(Curso, "full"), SujetoDelPlan.De(Ref.Create("educacion.curso", " gestion-agil "), " full "));
    }
}
