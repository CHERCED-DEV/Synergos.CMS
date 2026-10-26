using System.Net;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Educacion.Contracts;
using Synergos.Bff.Educacion.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// Cómo se llaman el curso y el plan en el aviso de la matrícula (NM-2), tras adoptar la oferta común del núcleo: lo que
/// la oferta común todavía no publica queda en el paso interino <c>educacion.nombres</c> y en su ruta (NM-3).
/// </summary>
public sealed class NombresDeCursosTests
{
    private static readonly Ref Curso = Ref.Create("educacion.curso", "gestion-agil");

    private static NombresDeCursos Almacen() => new(Path.Combine(Path.GetTempPath(), "nombres-" + Guid.NewGuid().ToString("N")));

    private static NombresDeCursos ConGestion(string? titulo = "Gestión ágil", string? plan = "Pago de contado")
    {
        var nombres = Almacen();
        Assert.True(nombres.Guardar("gestion-agil", new NombresDelCursoRequest(titulo, [new NombreDelPlanDto("full", plan)])).IsOk);
        return nombres;
    }

    private static async Task<(string? Curso, string? Plan)> Nombres(string? plan, NombresDeCursos? nombres = null)
    {
        var salida = await new PasoNombresDelCurso(nombres).EjecutarAsync(
            new EntradaDePaso("s-1", Ref.Create("prueba.flujo", "s-1"),
                new PasoDef("nombres", "educacion.nombres", ["curso", "plan"], ["nombreDelCurso", "nombreDelPlan"], null, null, null, null, null),
                new FlowContext().Set("curso", Curso).Set("plan", plan), null, null),
            CancellationToken.None);
        Assert.Equal(2, salida.Valores.Count);
        return ((string?)salida.Valores[0], (string?)salida.Valores[1]);
    }

    [Fact]
    public async Task El_paso_dice_el_titulo_del_curso_y_el_nombre_del_plan_publicados()
    {
        Assert.Equal(("Gestión ágil", "Pago de contado"), await Nombres("full", ConGestion()));
    }

    [Fact]
    public async Task Sin_nombres_publicados_cae_a_la_clave_del_curso_y_al_codigo_del_plan()
    {
        Assert.Equal(("gestion-agil", "full"), await Nombres("full"));
        Assert.Equal(("gestion-agil", "full"), await Nombres("full", ConGestion(titulo: null, plan: null)));
        Assert.Equal(("Gestión ágil", "premium"), await Nombres("premium", ConGestion()));
    }

    [Fact]
    public async Task Sin_plan_el_plan_no_tiene_nombre()
    {
        Assert.Equal(("Gestión ágil", null), await Nombres(null, ConGestion()));
        Assert.Equal(("Gestión ágil", null), await Nombres("  ", ConGestion()));
    }

    [Fact]
    public void Publicarlos_otra_vez_los_reemplaza()
    {
        var nombres = ConGestion();

        Assert.True(nombres.Guardar("gestion-agil", new NombresDelCursoRequest("Gestión ágil, 2.ª edición", [])).IsOk);

        var guardados = nombres.Buscar("gestion-agil")!;
        Assert.Equal("Gestión ágil, 2.ª edición", guardados.Nombre);
        Assert.Empty(guardados.Planes);
    }

    [Theory]
    [InlineData("otro/curso", "full", "Ok")]
    [InlineData("gestion-agil", "full/../otro", "Ok")]
    [InlineData("gestion-agil", "full", "largo")]
    public void Lo_que_no_cabe_se_rechaza_sin_guardar_nada(string curso, string plan, string nombreDelPlan)
    {
        var nombres = Almacen();
        var nombre = nombreDelPlan == "largo" ? new string('x', NombresDeCursos.NombreMaximo + 1) : nombreDelPlan;

        var r = nombres.Guardar(curso, new NombresDelCursoRequest("Título", [new NombreDelPlanDto(plan, nombre)]));

        Assert.Equal(NombresDeCursos.Invalidos, r.Rejection?.Code);
        Assert.Null(nombres.Buscar(curso));
    }

    [Fact]
    public void Un_plan_dos_veces_o_un_titulo_largo_se_rechazan()
    {
        var nombres = Almacen();

        Assert.Equal(NombresDeCursos.Invalidos, nombres.Guardar("gestion-agil",
            new NombresDelCursoRequest("T", [new NombreDelPlanDto("full", "A"), new NombreDelPlanDto("full", "B")])).Rejection?.Code);
        Assert.Equal(NombresDeCursos.Invalidos, nombres.Guardar("gestion-agil",
            new NombresDelCursoRequest(new string('x', NombresDeCursos.NombreMaximo + 1))).Rejection?.Code);
    }

    [Fact]
    public async Task La_ruta_guarda_los_nombres_con_su_llave_y_sin_llave_no()
    {
        using var r = new MatriculaDeCursoReal();

        var (sinLlave, _) = await r.PublicarNombres(llave: null);
        var (estado, cuerpo) = await r.PublicarNombres();
        var (mal, rechazo) = await r.PublicarNombres(curso: "otro..curso!", llave: "nombres-2");

        Assert.Equal(HttpStatusCode.BadRequest, sinLlave);
        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(MatriculaDeCursoReal.Titulo, cuerpo.GetProperty("nombre").GetString());
        Assert.Equal(["full", "premium"], cuerpo.GetProperty("planes").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, mal);
        Assert.Equal(NombresDeCursos.Invalidos, rechazo.GetProperty("code").GetString());
    }
}
