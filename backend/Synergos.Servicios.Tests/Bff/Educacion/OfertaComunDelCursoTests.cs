using System.Net;
using System.Text.Json;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// La matrícula sobre la oferta común del núcleo (001-P11 A con N1): el curso es la pieza, cada plan una variante, y la
/// línea a cotizar la compone <c>forma.una-linea-de-oferta</c> con el MISMO nombre con que la oferta publicó el precio.
/// Contra precios, pagos y avisos reales.
/// </summary>
public sealed class OfertaComunDelCursoTests
{
    private static decimal Total(JsonElement r) => r.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    [Fact]
    public async Task El_plan_se_cotiza_como_variante_del_curso_con_el_nombre_comun()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (_, sinPlan) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: null));
        var (_, premium) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "premium"));

        Assert.Equal(MatriculaDeCursoReal.Precio, Total(sinPlan));
        Assert.Equal(MatriculaDeCursoReal.Premium, Total(premium));
        Assert.Equal(Ref.Create("educacion.curso.variante", $"{MatriculaDeCursoReal.Curso}/premium"),
            SujetoDeLaOferta.De(Ref.Create("educacion.curso", MatriculaDeCursoReal.Curso), "premium"));
    }

    [Fact]
    public async Task El_plan_que_sale_al_republicar_deja_de_valer_sin_autorizar_nada()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (republicada, _) = await r.Publicar(MatriculaDeCursoReal.Oferta(planes: [("full", MatriculaDeCursoReal.Precio)]), llave: "oferta-2");
        var (estado, cuerpo) = await r.Abrir(llave: "m-premium", cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "premium"));
        var (_, contado) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "full"));

        Assert.Equal(HttpStatusCode.OK, republicada);
        Assert.True(estado == HttpStatusCode.Conflict, cuerpo.ToString());
        Assert.Equal("pricing.price_not_in_effect", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("m-premium", "cobroId"));
        Assert.Equal(MatriculaDeCursoReal.Precio, Total(contado));
    }

    [Fact]
    public async Task Un_plan_que_la_oferta_nunca_publico_no_tiene_precio()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (estado, cuerpo) = await r.Abrir(llave: "m-cuotas", cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "emi-3"));

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("m-cuotas", "cobroId"));
    }

    [Fact]
    public async Task Retirar_la_oferta_deja_el_curso_y_sus_planes_sin_precio_y_republicarla_los_devuelve()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (retirada, cuerpo) = await r.Retirar();
        var (_, sinCurso) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: null));
        var (_, sinPlan) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "full"));

        Assert.True(retirada == HttpStatusCode.OK, cuerpo.ToString());
        Assert.True(cuerpo.GetProperty("retirada").GetBoolean());
        Assert.Equal("pricing.price_not_in_effect", sinCurso.GetProperty("code").GetString());
        Assert.Equal("pricing.price_not_in_effect", sinPlan.GetProperty("code").GetString());

        var (otraVez, _) = await r.Publicar(llave: "oferta-3");
        var (_, vuelve) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "full"));
        Assert.Equal(HttpStatusCode.OK, otraVez);
        Assert.Equal(MatriculaDeCursoReal.Precio, Total(vuelve));
    }

    [Fact]
    public async Task Una_clave_con_otra_forma_la_rechaza_la_linea_sin_llegar_a_precios()
    {
        var precios = 0;
        using var r = await new MatriculaDeCursoReal(falla: p => { if (MatriculaDeCursoReal.Es(p, "POST", "^/v1/quotes")) precios++; return null; })
            .ConOferta();

        var (estado, cuerpo) = await r.Abrir(llave: "m-rara", cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "full/../otro"));

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(PasoUnaLineaDeOferta.LineaInvalida, cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, precios);
    }
}
