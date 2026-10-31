using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Bff.Educacion;
using Synergos.CMS.Tests.Bff.Viajes;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Personal;

/// <summary>
/// La saga de un flujo del personal es del FLUJO, no de la persona (núcleo, NS-3 de 013, 001-P12): la misma llave lleva a
/// la misma saga sea quien sea la persona del personal que la pide, así que un corte que deja la respuesta en el aire y
/// otra persona que reintenta no devuelven la plata dos veces. Contra Payments real, en los tres flujos del personal que
/// la mueven: la devolución de la tienda, la de la matrícula y la cancelación del viaje (que además suelta cupos).
/// </summary>
/// <remarks>
/// Lo que se afirma es el invariante, leído en la capacidad (<c>refunded</c> y <c>refunds</c>), y no cuántas llamadas
/// salieron: la llave de cada paso se deriva de la saga, y Payments devuelve una vez por llave.
/// </remarks>
public sealed class LaSagaDelPersonalTests
{
    private static string Id(JsonElement cuerpo) => cuerpo.GetProperty("id").GetString()!;

    private static decimal Devuelto(JsonElement pago) => pago.GetProperty("refunded").GetProperty("amount").GetDecimal();

    private static int Devoluciones(JsonElement pago) => pago.GetProperty("refunds").GetArrayLength();

    /// <summary>Una compra de la canasta de siempre (95.000), abierta y cerrada por su comprador.</summary>
    private static async Task<(string Id, string Cobro)> CompraCompletada(CompraDeTiendaReal t)
    {
        var (_, abierta) = await t.Abrir();
        var id = Id(abierta);
        var (estado, cerrada) = await t.Cerrar(id);
        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        return (id, t.Dato(id, "cobroId"));
    }

    private static Task<JsonElement> Pago(CompraDeTiendaReal t, string cobro) => t.Leer("payments", $"v1/payments/{cobro}");

    /// <summary>Una operación de <c>tienda.devolucion</c> pedida por <paramref name="personal"/>, como la reenvía la puerta.</summary>
    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> DelPersonal(CompraDeTiendaReal t, string ruta, string personal)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"tienda.personal:{personal}");
        using var r = await t.Orquestador.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    [Fact]
    public async Task Otra_persona_del_personal_que_reintenta_la_misma_devolucion_cae_en_la_misma_saga_y_Payments_devuelve_una_vez()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, cobro) = await CompraCompletada(t);

        // La respuesta de la primera se pierde, y otra persona reintenta la misma RMA.
        var (primero, de1) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1", personal: "staff-1");
        var (segundo, de2) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1", personal: "staff-2");

        Assert.True(primero == HttpStatusCode.Created, de1.ToString());
        Assert.True(segundo is HttpStatusCode.Created or HttpStatusCode.OK, de2.ToString());
        Assert.Equal(Id(de1), Id(de2));
        var pago = await Pago(t, cobro);
        Assert.Equal(20_000m, Devuelto(pago));
        Assert.Equal(1, Devoluciones(pago));
    }

    [Fact]
    public async Task La_carrera_de_dos_personas_del_personal_con_la_misma_llave_devuelve_una_sola_vez()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, cobro) = await CompraCompletada(t);

        // Seis a la vez, alternando quién: la que no tiene el turno contesta «ocupada» (transitorio) y se reintenta,
        // como lo haría quien la pidió.
        async Task<JsonElement> HastaQueSalga(string personal)
        {
            for (var intento = 0; ; intento++)
            {
                var (estado, cuerpo) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1", personal);
                if (estado is HttpStatusCode.Created or HttpStatusCode.OK) return cuerpo;
                Assert.True(intento < 20 && cuerpo.GetProperty("transient").GetBoolean(), $"{(int)estado} {cuerpo}");
                await Task.Delay(25);
            }
        }
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => HastaQueSalga(i % 2 == 0 ? "staff-1" : "staff-2")));

        Assert.Single(respuestas.Select(Id).Distinct());
        var pago = await Pago(t, cobro);
        Assert.Equal(20_000m, Devuelto(pago));
        Assert.Equal(1, Devoluciones(pago));
        Assert.Equal(75_000m, pago.GetProperty("refundable").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Otra_persona_del_personal_consulta_la_devolucion_y_quien_la_abrio_queda_como_su_duena()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, _) = await CompraCompletada(t);
        var (_, devolucion) = await t.Devolver(compra, CompraDeTiendaReal.Pesos(20_000m), "rma-1", personal: "staff-1");
        var id = Id(devolucion);

        var (consulta, vista) = await DelPersonal(t, $"v1/flujos/tienda.devolucion/{id}", "staff-2");
        var (_, deStaff1) = await DelPersonal(t, "v1/flujos/tienda.devolucion", "staff-1");
        var (_, deStaff2) = await DelPersonal(t, "v1/flujos/tienda.devolucion", "staff-2");

        Assert.Equal(HttpStatusCode.OK, consulta);
        Assert.Equal(id, Id(vista));
        // El rastro: la dueña es quien la abrió, y «listar» son las que abrió cada una.
        Assert.Equal(Ref.Create("tienda.personal", "staff-1"), t.Sagas.Find(id)!.Dueno);
        Assert.Equal(1, deStaff1.GetProperty("total").GetInt32());
        Assert.Equal(0, deStaff2.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task En_la_compra_del_comprador_la_llave_sigue_siendo_de_su_dueno()
    {
        using var t = new CompraDeTiendaReal();

        var (_, deU1) = await t.Abrir(llave: "compra-1", sujeto: "u-1");
        var (_, deU2) = await t.Abrir(llave: "compra-1", sujeto: "u-2");
        var (consulta, _) = await t.Consultar(Id(deU1), sujeto: "u-2");

        Assert.NotEqual(Id(deU1), Id(deU2));
        Assert.Equal(HttpStatusCode.NotFound, consulta);
    }

    [Fact]
    public async Task Dos_administradores_que_devuelven_la_misma_matricula_la_devuelven_una_vez()
    {
        using var t = await new MatriculaDeCursoReal().ConOferta();
        var matricula = await t.Completada();

        var (primero, de1) = await t.Devolver(matricula, "devolucion-1", personal: "staff-1");
        var (segundo, de2) = await t.Devolver(matricula, "devolucion-1", personal: "staff-2");

        Assert.True(primero == HttpStatusCode.Created, de1.ToString());
        Assert.True(segundo is HttpStatusCode.Created or HttpStatusCode.OK, de2.ToString());
        Assert.Equal(Id(de1), Id(de2));
        var pago = await t.Leer("payments", $"v1/payments/{t.Dato(matricula, "cobroId")}");
        Assert.Equal(MatriculaDeCursoReal.Precio, Devuelto(pago));
        Assert.Equal(1, Devoluciones(pago));
    }

    [Fact]
    public async Task Dos_personas_que_cancelan_el_mismo_viaje_confirmado_cancelan_y_devuelven_una_vez()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierto) = await r.Abrir(llave: "viaje-1");
        var viaje = Id(abierto);
        var (cerrado, cuerpo) = await r.Cerrar(viaje);
        Assert.True(cerrado == HttpStatusCode.OK, cuerpo.ToString());

        // Con 800.000 de penalidad se devuelven 400.000 de 1.200.000: dos devoluciones cabrían.
        var (primero, de1) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(800_000m), "cancelacion-1", personal: "staff-1");
        var (segundo, de2) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(800_000m), "cancelacion-1", personal: "staff-2");

        Assert.True(primero == HttpStatusCode.Created, de1.ToString());
        Assert.True(segundo is HttpStatusCode.Created or HttpStatusCode.OK, de2.ToString());
        Assert.Equal(Id(de1), Id(de2));
        var pago = await r.Leer("payments", $"v1/payments/{r.Dato(viaje, "cobroId")}");
        Assert.Equal(400_000m, Devuelto(pago));
        Assert.Equal(1, Devoluciones(pago));
        Assert.All(await r.Reservas(), x => Assert.Equal("Cancelled", x.Estado));
    }
}
