using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Synergos.Bff.Core.Flow;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// La devolución del personal (lote 8, 010-P2 B, T033): devolver una matrícula pagada es un flujo POSTERIOR,
/// <c>educacion.devolucion</c>, del personal, que lee el cobro y el total de la matrícula con <c>flow.leer-original</c> y
/// lo devuelve con <c>payments.devolver</c> — contra Payments real. Cero líneas de C#: el molde de
/// <c>tienda.devolucion</c>, con el total leído de la original como <c>alquiler.cancelacion</c>.
/// </summary>
/// <remarks>
/// Lo que se mira es lo que la capacidad dice haber devuelto (<c>refunded</c>, <c>refunds</c>), no lo que contestó el
/// flujo; y la matrícula original no se toca.
/// </remarks>
public sealed class DevolucionDeLaMatriculaTests
{
    private static async Task<JsonElement> Pago(MatriculaDeCursoReal t, string matricula)
        => await t.Leer("payments", $"v1/payments/{t.Dato(matricula, "cobroId")}");

    private static decimal Devuelto(JsonElement pago) => pago.GetProperty("refunded").GetProperty("amount").GetDecimal();

    /// <summary>Un arnés que anota cada llamada a pagos (método y ruta) y la deja pasar.</summary>
    private static (MatriculaDeCursoReal Arnes, ConcurrentQueue<string> Pagos) ConLasLlamadasAPagos()
    {
        var pagos = new ConcurrentQueue<string>();
        var arnes = new MatriculaDeCursoReal(falla: r =>
        {
            if (r.RequestUri!.AbsolutePath.StartsWith("/v1/payments", StringComparison.Ordinal)) pagos.Enqueue($"{r.Method} {r.RequestUri.AbsolutePath}");
            return null;
        });
        return (arnes, pagos);
    }

    [Fact]
    public async Task El_personal_devuelve_el_total_cobrado_y_la_matricula_original_queda_intacta()
    {
        using var t = await new MatriculaDeCursoReal().ConOferta();
        var matricula = await t.Completada();
        var cobro = t.Dato(matricula, "cobroId");

        var (estado, devolucion) = await t.Devolver(matricula);

        Assert.True(estado == HttpStatusCode.Created, devolucion.ToString());
        Assert.Equal("Completed", devolucion.GetProperty("estado").GetString());
        Assert.Equal(MatriculaDeCursoReal.Precio, devolucion.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        var pago = await Pago(t, matricula);
        Assert.Equal(MatriculaDeCursoReal.Precio, Devuelto(pago));
        Assert.Equal(1, pago.GetProperty("refunds").GetArrayLength());

        // La original no se toca: sigue completada, de su alumno, con su cobro.
        var (_, original) = await t.Consultar(matricula);
        Assert.Equal("Completed", original.GetProperty("estado").GetString());
        Assert.Equal(cobro, t.Dato(matricula, "cobroId"));
    }

    [Fact]
    public async Task Reintentar_la_misma_devolucion_no_devuelve_dos_veces_y_otra_devolucion_se_rechaza()
    {
        using var t = await new MatriculaDeCursoReal().ConOferta();
        var matricula = await t.Completada();

        var (_, primera) = await t.Devolver(matricula, "devolucion-1");
        var (repetida, otraVez) = await t.Devolver(matricula, "devolucion-1");
        var (segunda, rechazo) = await t.Devolver(matricula, "devolucion-2", personal: "staff-2");

        Assert.True(repetida is HttpStatusCode.OK or HttpStatusCode.Created, otraVez.ToString());
        Assert.Equal(primera.GetProperty("id").GetString(), otraVez.GetProperty("id").GetString());
        Assert.NotEqual(HttpStatusCode.Created, segunda);
        Assert.Equal("payments.refund_over_captured", rechazo.GetProperty("code").GetString());
        var pago = await Pago(t, matricula);
        Assert.Equal(MatriculaDeCursoReal.Precio, Devuelto(pago));
        Assert.Equal(1, pago.GetProperty("refunds").GetArrayLength());
    }

    [Fact]
    public async Task Una_matricula_no_completada_no_se_devuelve_deshacerla_es_de_la_compensacion()
    {
        var (arnes, pagos) = ConLasLlamadasAPagos();
        using var t = arnes;
        await t.ConOferta();
        var (_, abierta) = await t.Abrir("matricula-abierta");
        var enCurso = abierta.GetProperty("id").GetString()!;
        var (_, otra) = await t.Abrir("matricula-cancelada", alumno: "u-2");
        var cancelada = otra.GetProperty("id").GetString()!;
        await t.Cancelar(cancelada, "u-2");

        var (estado1, r1) = await t.Devolver(enCurso, "d-1");
        var (estado2, r2) = await t.Devolver(cancelada, "d-2");
        var (estado3, r3) = await t.Devolver("no-existe", "d-3");

        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (estado1, r1.GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (estado2, r2.GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.NotFound, PasoLeerOriginal.NoEncontrada), (estado3, r3.GetProperty("code").GetString()));
        Assert.DoesNotContain(pagos, l => l.EndsWith("/refund", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_matricula_de_total_cero_no_tiene_cobro_que_devolver_y_no_llama_a_pagos()
    {
        var (arnes, pagos) = ConLasLlamadasAPagos();
        using var t = arnes;
        await t.ConOferta(MatriculaDeCursoReal.Oferta("intro-ia", 0m, ("free", 0m)));
        var matricula = await t.Completada("matricula-intro-ia", cuerpo: MatriculaDeCursoReal.Cuerpo("intro-ia", "free"));

        var (estado, rechazo) = await t.Devolver(matricula);

        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.SinDato), (estado, rechazo.GetProperty("code").GetString()));
        Assert.Empty(pagos);
    }
}
