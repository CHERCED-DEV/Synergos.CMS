using System.Net;
using System.Text.Json;
using Synergos.Bff.Core.Flow;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La historia 7 en el orquestador (004-tienda-compra, T039/T040; núcleo P6 = A y P8 = A del lote 6): devolver parte
/// de una compra de la puerta es un flujo POSTERIOR, <c>tienda.devolucion</c>, del personal, que lee el cobro de la
/// compra con <c>flow.leer-original</c> y lo devuelve con <c>payments.devolver</c> — contra Payments real.
/// </summary>
/// <remarks>
/// Lo que se mira es lo que la capacidad dice haber devuelto (<c>refunded</c>, <c>refundable</c>, <c>refunds</c>), no
/// lo que contestó el flujo: el tope del acumulado es el de <c>Api.Payments</c>, y la compra original no se toca.
/// </remarks>
public sealed class ReembolsoDeTiendaTests
{
    /// <summary>Una compra de la canasta de siempre (95.000), abierta y cerrada por su comprador.</summary>
    private static async Task<(string Id, string Cobro)> Completada(CompraDeTiendaReal t, string llave = "compra-1")
    {
        var (_, abierta) = await t.Abrir(llave: llave);
        var id = abierta.GetProperty("id").GetString()!;
        var (estado, cerrada) = await t.Cerrar(id);
        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        return (id, t.Dato(id, "cobroId"));
    }

    private static async Task<JsonElement> Pago(CompraDeTiendaReal t, string cobro) => await t.Leer("payments", $"v1/payments/{cobro}");

    private static decimal Devuelto(JsonElement pago) => pago.GetProperty("refunded").GetProperty("amount").GetDecimal();

    [Fact]
    public async Task El_personal_devuelve_una_parte_de_una_compra_completada_y_la_capacidad_dice_cuanto()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, cobro) = await Completada(t);

        var (estado, devolucion) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1");

        Assert.True(estado == HttpStatusCode.Created, devolucion.ToString());
        Assert.Equal("Completed", devolucion.GetProperty("estado").GetString());
        Assert.Equal(20_000m, devolucion.GetProperty("datos").GetProperty("monto").GetProperty("amount").GetDecimal());
        var pago = await Pago(t, cobro);
        Assert.Equal("Captured", pago.GetProperty("status").GetString());
        Assert.Equal(20_000m, Devuelto(pago));
        Assert.Equal(75_000m, pago.GetProperty("refundable").GetProperty("amount").GetDecimal());

        // La original no se toca: sigue completada, de su comprador, con su cobro.
        var (_, original) = await t.Consultar(compra);
        Assert.Equal("Completed", original.GetProperty("estado").GetString());
        Assert.Equal(cobro, original.GetProperty("datos").GetProperty("cobroId").GetString());
    }

    [Fact]
    public async Task Dos_parciales_que_se_pasarian_rechazan_el_segundo_sin_devolver_nada_de_el()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, cobro) = await Completada(t);

        var (primero, _) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(60_000m), "rma-1");
        var (segundo, rechazo) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(40_000m), "rma-2");

        Assert.Equal(HttpStatusCode.Created, primero);
        Assert.NotEqual(HttpStatusCode.Created, segundo);
        Assert.Equal("payments.refund_over_captured", rechazo.GetProperty("code").GetString());
        var pago = await Pago(t, cobro);
        Assert.Equal(60_000m, Devuelto(pago));
        Assert.Equal(1, pago.GetProperty("refunds").GetArrayLength());

        // Lo que cabe, sí: los 35.000 que quedan.
        var (tercero, _) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(35_000m), "rma-3");
        Assert.Equal(HttpStatusCode.Created, tercero);
        Assert.Equal(95_000m, Devuelto(await Pago(t, cobro)));
    }

    [Fact]
    public async Task Reintentar_la_misma_devolucion_no_devuelve_dos_veces()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, cobro) = await Completada(t);

        var (_, primera) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1");
        var (estado, otra) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1");

        Assert.True(estado is HttpStatusCode.OK or HttpStatusCode.Created, otra.ToString());
        Assert.Equal(primera.GetProperty("id").GetString(), otra.GetProperty("id").GetString());
        var pago = await Pago(t, cobro);
        Assert.Equal(20_000m, Devuelto(pago));
        Assert.Equal(1, pago.GetProperty("refunds").GetArrayLength());
        Assert.Equal(1, t.Llamadas.Count(l => l.StartsWith("POST /v1/payments/", StringComparison.Ordinal) && l.EndsWith("/refund", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Una_compra_no_completada_no_se_reembolsa_deshacerla_es_de_la_compensacion()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir(llave: "abierta");
        var enCurso = abierta.GetProperty("id").GetString()!;
        var (_, otra) = await t.Abrir(llave: "cancelada");
        var cancelada = otra.GetProperty("id").GetString()!;
        await t.Cancelar(cancelada);

        var (estado1, r1) = await t.Devolver(enCurso, CompraDeTiendaReal.Pesos(10_000m), "rma-1");
        var (estado2, r2) = await t.Devolver(cancelada, CompraDeTiendaReal.Pesos(10_000m), "rma-2");
        var (estado3, r3) = await t.Devolver("no-existe", CompraDeTiendaReal.Pesos(10_000m), "rma-3");

        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (estado1, r1.GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (estado2, r2.GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.NotFound, PasoLeerOriginal.NoEncontrada), (estado3, r3.GetProperty("code").GetString()));
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/refund", StringComparison.Ordinal));
        Assert.Equal("Authorized", (await Pago(t, t.Dato(enCurso, "cobroId"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Una_compra_de_total_cero_no_tiene_cobro_que_devolver()
    {
        using var t = new CompraDeTiendaReal(precios: new Dictionary<string, decimal> { ["p-1"] = 0m });
        var (_, abierta) = await t.Abrir([CompraDeTiendaReal.Linea("p-1", 1)]);
        var compra = abierta.GetProperty("id").GetString()!;
        await t.Cerrar(compra);

        var (estado, rechazo) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(1_000m), "rma-1");

        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.SinDato), (estado, rechazo.GetProperty("code").GetString()));
        Assert.DoesNotContain(t.Llamadas, l => l.StartsWith("POST /v1/payments", StringComparison.Ordinal));
    }

    /// <summary>
    /// «Sin tocar la plata»: el monto y la moneda los valida quien tiene el cobro —sólo Payments sabe en qué moneda
    /// se capturó—, y el flujo pasa su rechazo tal cual. Medido: los tres llegan a <c>/refund</c> y no devuelven nada.
    /// </summary>
    [Theory]
    [InlineData(10_000, "USD", "payments.refund_currency_mismatch")]
    [InlineData(0, "COP", "payments.bad_refund_amount")]
    [InlineData(-5_000, "COP", "payments.bad_refund_amount")]
    public async Task Otra_moneda_cero_o_negativo_se_rechazan_sin_devolver_nada(int monto, string moneda, string codigo)
    {
        using var t = new CompraDeTiendaReal();
        var (compra, cobro) = await Completada(t);

        var (estado, rechazo) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(monto, moneda), "rma-1");

        Assert.Equal((HttpStatusCode.BadRequest, codigo), (estado, rechazo.GetProperty("code").GetString()));
        Assert.Equal(0m, Devuelto(await Pago(t, cobro)));
    }
}
