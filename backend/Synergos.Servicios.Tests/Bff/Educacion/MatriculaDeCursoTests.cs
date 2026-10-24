using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Bff.Reservas;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// Historias 1 y 2 de 010-educacion-matricula (T010, T020): matricularse a un curso de pago o gratis por la puerta,
/// contra precios, pagos y avisos reales.
/// </summary>
public sealed class MatriculaDeCursoTests
{
    private static decimal Total(JsonElement r) => r.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    [Fact]
    public async Task Abrir_autoriza_el_total_cotizado_cerrar_captura_y_avisa_una_vez()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        var (_, cotizada) = await r.Cotizar(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "premium"));

        var (estado, abierta) = await r.Abrir(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "premium"));

        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        var id = abierta.GetProperty("id").GetString()!;
        Assert.Equal(Total(cotizada), Total(abierta));
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");
        Assert.Equal("Authorized", cobro.GetProperty("status").GetString());
        Assert.Equal(MatriculaDeCursoReal.Premium, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", await r.Cobro(id));
        Assert.Equal(1, await r.Avisos());
    }

    [Fact]
    public async Task Repetir_el_cierre_no_cobra_ni_avisa_dos_veces()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        await r.Cerrar(id);

        var (otraVez, cerrada) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, otraVez);
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", await r.Cobro(id));
        Assert.Equal(1, await r.Avisos());
    }

    [Fact]
    public async Task La_respuesta_dice_curso_plan_y_total_y_ningun_identificador_de_las_capacidades()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var (_, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        var datos = cerrada.GetProperty("datos");
        Assert.Equal(["curso", "plan", "total"], datos.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("educacion.curso", datos.GetProperty("curso").GetProperty("kind").GetString());
        Assert.Equal(MatriculaDeCursoReal.Curso, datos.GetProperty("curso").GetProperty("id").GetString());
        Assert.Equal("full", datos.GetProperty("plan").GetString());
        Assert.DoesNotContain("cobroId", cerrada.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_matricula_de_otro_alumno_no_existe_para_quien_la_pide()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        var (_, abierta) = await r.Abrir(alumno: "ana", llave: "matricula-ana");
        var id = abierta.GetProperty("id").GetString()!;

        var (consulta, cuerpo) = await r.Consultar(id, alumno: "beto");
        var (cierre, _) = await r.Cerrar(id, alumno: "beto");

        Assert.Equal(HttpStatusCode.NotFound, consulta);
        Assert.Equal("educacion.saga_not_found", cuerpo.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, cierre);
        Assert.Equal("Authorized", await r.Cobro(id));
    }

    [Fact]
    public async Task El_plan_de_otro_curso_no_tiene_precio_y_no_se_autoriza_nada()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        await r.ConOferta(MatriculaDeCursoReal.Oferta("taller-barato", 10_000m, ("promo", 10_000m)));

        // El plan barato existe, pero es del OTRO curso: precios no tiene «gestion-agil/promo».
        var (estado, cuerpo) = await r.Abrir(llave: "m-trampa", cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "promo"));

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("m-trampa", "cobroId"));
    }

    [Fact]
    public async Task Una_referencia_de_otra_clase_la_rechaza_el_host_sin_llamar_a_nadie()
    {
        var pagos = 0;
        using var r = await new MatriculaDeCursoReal(falla: p => { if (MatriculaDeCursoReal.Es(p, "POST", "^/v1/(quotes|payments)")) pagos++; return null; })
            .ConOferta();

        var (estado, cuerpo) = await r.Abrir(cuerpo: MatriculaDeCursoReal.Cuerpo(curso: "doc-demo-1", clase: "salud.profesional"));

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("educacion.entrada_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, pagos);
    }

    [Fact]
    public async Task Cada_llamada_de_abrir_y_de_cerrar_lleva_el_numero_de_la_matricula()
    {
        var registro = new RegistroConAmbitos();
        using var r = await new MatriculaDeCursoReal(registro: registro).ConOferta();
        lock (registro.Lineas) registro.Lineas.Clear();

        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        await r.Cerrar(id);

        var numero = abierta.GetProperty("numero").GetString()!;
        var llamadas = registro.Lineas.Where(l => l.Categoria == MatriculaDeCursoReal.LlamadasAlArnes).ToList();
        foreach (var ruta in new[] { "/v1/quotes", "/v1/payments", "/capture", "/v1/deliveries" })
        {
            Assert.Contains(llamadas, l => l.Mensaje.Contains(ruta, StringComparison.Ordinal));
        }
        Assert.All(llamadas, l =>
        {
            Assert.True(numero.Equals(l.Valores.GetValueOrDefault("NumeroDeSaga")), $"«{l.Mensaje}» no lleva el número {numero}.");
            Assert.DoesNotContain("ana@ejemplo.co", string.Join(' ', l.Textos.Append(l.Mensaje)), StringComparison.Ordinal);
        });
    }

    // ── Historia 2: el curso gratis (T020) ─────────────────────────────────

    [Fact]
    public async Task Un_curso_gratis_queda_activo_sin_llamar_a_pagos_y_avisa()
    {
        var pagos = 0;
        using var r = await new MatriculaDeCursoReal(falla: p => { if (MatriculaDeCursoReal.Es(p, "POST", "^/v1/payments")) pagos++; return null; })
            .ConOferta(MatriculaDeCursoReal.Oferta("taller-gratis", 0m, ("free", 0m)));

        var (_, abierta) = await r.Abrir(llave: "m-gratis", cuerpo: MatriculaDeCursoReal.Cuerpo("taller-gratis", "free"));
        var id = abierta.GetProperty("id").GetString()!;
        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(0m, Total(cerrada));
        Assert.False(r.Tiene(id, "cobroId"));
        Assert.Equal(0, pagos);
        Assert.Equal(1, await r.Avisos());
    }

    [Fact]
    public async Task Un_plan_de_total_cero_de_un_curso_de_pago_tampoco_cobra()
    {
        var pagos = 0;
        using var r = await new MatriculaDeCursoReal(falla: p => { if (MatriculaDeCursoReal.Es(p, "POST", "^/v1/payments")) pagos++; return null; })
            .ConOferta(MatriculaDeCursoReal.Oferta(planes: [("full", MatriculaDeCursoReal.Precio), ("beca", 0m)]));

        var (_, abierta) = await r.Abrir(llave: "m-beca", cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "beca"));
        var (_, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(0, pagos);
    }

    [Fact]
    public async Task Un_curso_de_pago_sin_oferta_se_rechaza_y_nunca_queda_gratis()
    {
        using var r = new MatriculaDeCursoReal();

        var (estado, cuerpo) = await r.Abrir(llave: "m-sin-oferta");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());
        Assert.NotEqual(SagaStatus.Completed, r.Sagas.Find("m-sin-oferta")?.Status);
        Assert.False(r.Tiene("m-sin-oferta", "cobroId"));
    }
}
