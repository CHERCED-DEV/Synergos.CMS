using System.Net;
using System.Text.Json;
using Synergos.Bff.Educacion.Domain;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// La oferta de los cursos (010-educacion-matricula T005 y T027, NM-1): lo que el CMS publica es lo único que la
/// matrícula puede cobrar, contra la capacidad de precios real.
/// </summary>
public sealed class OfertaDeCursosTests
{
    /// <summary>Lo que contesta precios por un precio retirado: deja de valer, no desaparece (y nunca es gratis).</summary>
    private const string Retirado = "pricing.price_not_in_effect";

    private static decimal Total(JsonElement cotizada) => cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(MatriculaDeCursoReal r, string? plan)
        => await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: plan));

    [Fact]
    public async Task Publica_el_precio_del_curso_y_el_de_cada_plan()
    {
        using var r = new MatriculaDeCursoReal();

        var (estado, oferta) = await r.Publicar();

        Assert.True(estado == HttpStatusCode.OK, oferta.ToString());
        Assert.Equal(3, oferta.GetProperty("precios").GetInt32());
        Assert.Equal(MatriculaDeCursoReal.Precio, Total((await Cotizar(r, null)).Cuerpo));
        Assert.Equal(MatriculaDeCursoReal.Precio, Total((await Cotizar(r, "full")).Cuerpo));
        Assert.Equal(MatriculaDeCursoReal.Premium, Total((await Cotizar(r, "premium")).Cuerpo));
    }

    [Fact]
    public async Task Repetir_la_misma_publicacion_no_cambia_nada()
    {
        using var r = new MatriculaDeCursoReal();
        var (_, primera) = await r.Publicar();

        var (estado, segunda) = await r.Publicar();

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(primera.ToString(), segunda.ToString());
        Assert.Equal(MatriculaDeCursoReal.Premium, Total((await Cotizar(r, "premium")).Cuerpo));
    }

    public static TheoryData<object, string> Invalidas() => new()
    {
        { new { curso = "otro/curso", precio = 1m, moneda = "COP" }, "curso" },
        { new { curso = "gestion-agil", precio = -1m, moneda = "COP" }, "precio" },
        { new { curso = "gestion-agil", precio = 1m, moneda = "CO" }, "moneda" },
        { new { curso = "gestion-agil", precio = 1m, moneda = "COP", planes = new[] { new { codigo = "a", total = -5m } } }, "planes.total" },
        { new { curso = "gestion-agil", precio = 1m, moneda = "COP", planes = new[] { new { codigo = "a", total = 1m }, new { codigo = "a", total = 2m } } }, "planes.codigo" },
        { new { curso = "gestion-agil", precio = 1m, moneda = "COP", planes = new[] { new { codigo = "a/b", total = 1m } } }, "planes.codigo" },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Una_oferta_mal_formada_no_publica_nada_y_nombra_el_campo(object oferta, string campo)
    {
        using var r = new MatriculaDeCursoReal();

        var (estado, cuerpo) = await r.Publicar(oferta);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(OfertaDeCursos.Invalida, cuerpo.GetProperty("code").GetString());
        Assert.Contains($"«{campo}»", cuerpo.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Cotizar(r, null)).Estado);
    }

    [Fact]
    public async Task Publicar_otra_vez_sin_un_plan_lo_deja_sin_precio()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (estado, _) = await r.Publicar(MatriculaDeCursoReal.Oferta(planes: ("full", MatriculaDeCursoReal.Precio)), llave: "oferta-2");

        Assert.Equal(HttpStatusCode.OK, estado);
        var (sinPrecio, cuerpo) = await Cotizar(r, "premium");
        Assert.Equal(HttpStatusCode.Conflict, sinPrecio);
        Assert.Equal(Retirado, cuerpo.GetProperty("code").GetString());
        Assert.Equal(MatriculaDeCursoReal.Precio, Total((await Cotizar(r, "full")).Cuerpo));
    }

    [Fact]
    public async Task Retirar_deja_sin_precio_al_curso_y_a_sus_planes_y_repetirlo_no_hace_dano()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (estado, retirada) = await r.Retirar();
        var (otraVez, _) = await r.Retirar(llave: "retiro-2");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.True(retirada.GetProperty("retirada").GetBoolean());
        Assert.Equal(0, retirada.GetProperty("precios").GetInt32());
        Assert.Equal(HttpStatusCode.OK, otraVez);
        foreach (var plan in new string?[] { null, "full", "premium" })
        {
            var (sinPrecio, cuerpo) = await Cotizar(r, plan);
            Assert.Equal(HttpStatusCode.Conflict, sinPrecio);
            Assert.Equal(Retirado, cuerpo.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Retirar_un_curso_que_nunca_se_publico_lo_dice()
    {
        using var r = new MatriculaDeCursoReal();

        var (estado, cuerpo) = await r.Retirar("nunca-publicado");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal(OfertaDeCursos.NoExiste, cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_curso_gratis_publica_su_cero_y_cotiza_cero()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta(MatriculaDeCursoReal.Oferta("taller-gratis", 0m, ("free", 0m)));

        var (estado, cotizada) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo("taller-gratis", "free"));

        Assert.True(estado == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(0m, Total(cotizada));
    }

    /// <summary>T027: el editor cambia el precio y vuelve a publicar; la matrícula cobra el nuevo.</summary>
    [Fact]
    public async Task Cambiar_el_precio_y_volver_a_publicar_cotiza_el_nuevo()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        await r.Publicar(MatriculaDeCursoReal.Oferta(precio: 280_000m, planes: ("full", 280_000m)), llave: "oferta-2");

        Assert.Equal(280_000m, Total((await Cotizar(r, "full")).Cuerpo));
    }

    /// <summary>
    /// T027: retirar el curso con una matrícula abierta no la deja colgada —cerrarla cobra lo que se cotizó al abrir—,
    /// y abrir otra se rechaza porque ya no hay precio.
    /// </summary>
    [Fact]
    public async Task Con_una_matricula_abierta_retirar_la_deja_cerrar_y_rechaza_abrir_otra()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        var (_, abierta) = await r.Abrir(alumno: "ana", llave: "matricula-ana");
        var id = abierta.GetProperty("id").GetString()!;

        await r.Retirar();
        var (cerro, cerrada) = await r.Cerrar(id, alumno: "ana");
        var (otra, cuerpo) = await r.Abrir(alumno: "beto", llave: "matricula-beto");

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(MatriculaDeCursoReal.Precio, Total(cerrada));
        Assert.Equal("Captured", await r.Cobro(id));
        Assert.Equal(HttpStatusCode.Conflict, otra);
        Assert.Equal(Retirado, cuerpo.GetProperty("code").GetString());
    }
}
