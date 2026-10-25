using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Historia 1 de 006-viajes-hoteles: reservar un viaje de varios ítems por la puerta, contra las capacidades reales
/// (agenda, precios, pagos), con <c>viajes.reserva</c> declarado (T010).
/// </summary>
public sealed class ViajesReservaRealTests
{
    private static string Id(JsonElement cuerpo) => cuerpo.GetProperty("id").GetString()!;

    private static decimal Monto(JsonElement cuerpo, string dato)
        => cuerpo.GetProperty("datos").GetProperty(dato).GetProperty("amount").GetDecimal();

    [Fact]
    public async Task Tres_items_se_apartan_y_se_autorizan_al_abrir_y_se_capturan_y_se_reservan_al_cerrar()
    {
        using var r = await new ViajesReservaReal().ConLosTres();

        var (abrio, abierta) = await r.Abrir();
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        Assert.Equal(1_200_000m, Monto(abierta, "total"));
        var id = Id(abierta);
        Assert.Equal(3, r.Apartados(id).Count);
        var cobro = r.Dato(id, "cobroId");
        var autorizado = await r.Leer("payments", $"v1/payments/{cobro}");
        Assert.Equal("Authorized", autorizado.GetProperty("status").GetString());
        Assert.Equal(1_200_000m, autorizado.GetProperty("amount").GetProperty("amount").GetDecimal());

        var (cerro, cerrada) = await r.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());

        // Una reserva por ítem, cada una con SU periodo: el del vuelo, las dos noches y los días del auto.
        var reservas = await r.Reservas();
        Assert.All(reservas, x => Assert.Equal("Confirmed", x.Estado));
        Assert.Equal(
            new[]
            {
                (ViajesReservaReal.Salida, ViajesReservaReal.Salida.AddHours(2)),
                (ViajesReservaReal.Salida.AddHours(2), ViajesReservaReal.Salida.AddDays(2)),
                (ViajesReservaReal.Salida.AddHours(5), ViajesReservaReal.Salida.AddDays(2).AddHours(1)),
            },
            reservas.Select(x => (x.Inicio, x.Fin)).Order().ToArray());
        Assert.Equal("Captured", (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task La_respuesta_trae_el_itinerario_el_desglose_y_el_primer_vencimiento_de_la_agenda()
    {
        using var r = await new ViajesReservaReal().ConLosTres();

        var (_, abierta) = await r.Abrir();

        var datos = abierta.GetProperty("datos");
        Assert.Equal(["descuento", "impuesto", "itinerario", "subtotal", "total", "vence"],
            datos.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var itinerario = datos.GetProperty("itinerario").EnumerateArray().ToList();
        Assert.Equal([ViajesReservaReal.Vuelo, ViajesReservaReal.Hotel, ViajesReservaReal.Auto],
            itinerario.Select(p => p.GetProperty("producto").GetString()!).ToArray());
        Assert.Equal("Bogotá → Cartagena", itinerario[0].GetProperty("etiqueta").GetString());
        // Sin etiqueta, la pantalla nombra el producto.
        Assert.Equal(ViajesReservaReal.Auto, itinerario[2].GetProperty("etiqueta").GetString());
        Assert.Equal(1_200_000m, Monto(abierta, "subtotal"));

        // El plazo lo dice la agenda: el del apartado que vence antes.
        var vencimientos = new List<DateTimeOffset>();
        foreach (var hold in r.Apartados(Id(abierta)))
        {
            vencimientos.Add((await r.Leer("booking", $"v1/holds/{hold}")).GetProperty("expiresAt").GetDateTimeOffset());
        }
        Assert.Equal(vencimientos.Min(), datos.GetProperty("vence").GetDateTimeOffset());

        // Ningún identificador de las capacidades sale por la puerta.
        Assert.DoesNotMatch(new Regex("apartadoId|recursoId|cobroId|holdId"), abierta.GetRawText());
    }

    [Fact]
    public async Task Un_hotel_es_un_viaje_de_un_item()
    {
        using var r = await new ViajesReservaReal().ConProducto(ViajesReservaReal.Hotel, 700_000m);
        var noches = ViajesReservaReal.Item(ViajesReservaReal.Hotel, ViajesReservaReal.Salida.AddHours(5), ViajesReservaReal.Salida.AddDays(2).AddHours(1));

        var (_, abierta) = await r.Abrir(items: [noches]);
        var (cerro, cerrada) = await r.Cerrar(Id(abierta));

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(700_000m, Monto(cerrada, "total"));
        Assert.Equal(["Confirmed"], (await r.Reservas()).Select(x => x.Estado).ToArray());
    }

    [Fact]
    public async Task Un_viaje_gratis_se_reserva_sin_cobro()
    {
        using var r = await new ViajesReservaReal().ConProducto(ViajesReservaReal.Hotel, 0m);
        var noches = ViajesReservaReal.Item(ViajesReservaReal.Hotel, ViajesReservaReal.Salida, ViajesReservaReal.Salida.AddDays(1));

        var (abrio, abierta) = await r.Abrir(items: [noches]);
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        Assert.False(r.Tiene(Id(abierta), "cobroId"));

        var (cerro, cerrada) = await r.Cerrar(Id(abierta));
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(["Confirmed"], (await r.Reservas()).Select(x => x.Estado).ToArray());
    }

    [Fact]
    public async Task Cancelar_antes_de_confirmar_suelta_cada_apartado_y_anula_el_cobro()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);
        var cobro = r.Dato(id, "cobroId");
        Assert.Equal(1, await r.Ocupados(ViajesReservaReal.Hotel, ViajesReservaReal.Salida.AddHours(5), ViajesReservaReal.Salida.AddDays(2)));

        var (cancelo, cancelada) = await r.Cancelar(id);

        Assert.True(cancelo == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Vuelo, ViajesReservaReal.Salida, ViajesReservaReal.Salida.AddHours(2)));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Hotel, ViajesReservaReal.Salida.AddHours(5), ViajesReservaReal.Salida.AddDays(2)));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Auto, ViajesReservaReal.Salida.AddHours(2), ViajesReservaReal.Salida.AddDays(2)));
        Assert.Equal("Voided", (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.Empty(await r.Reservas());
    }

    [Fact]
    public async Task El_doble_clic_en_abrir_y_en_cerrar_no_aparta_ni_cobra_ni_reserva_dos_veces()
    {
        using var r = await new ViajesReservaReal().ConLosTres();

        var (_, primera) = await r.Abrir();
        var (otra, segunda) = await r.Abrir();
        Assert.True((int)otra is >= 200 and < 300, segunda.ToString());
        Assert.Equal(Id(primera), Id(segunda));
        Assert.Equal(1, await r.Ocupados(ViajesReservaReal.Vuelo, ViajesReservaReal.Salida, ViajesReservaReal.Salida.AddHours(2)));
        var cobro = r.Dato(Id(primera), "cobroId");

        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(Id(primera))).Estado);
        var (repetido, cuerpo) = await r.Cerrar(Id(primera));

        Assert.Equal(HttpStatusCode.OK, repetido);
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(3, (await r.Reservas()).Count);
        Assert.Equal(cobro, r.Dato(Id(primera), "cobroId"));
        Assert.Equal("Captured", (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task El_viaje_de_otro_no_existe_para_leerlo_cerrarlo_ni_cancelarlo()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        foreach (var (estado, cuerpo) in new[] { await r.Consultar(id, "u-2"), await r.Cerrar(id, "u-2"), await r.Cancelar(id, "u-2") })
        {
            Assert.Equal(HttpStatusCode.NotFound, estado);
            Assert.Equal("viajes.saga_not_found", cuerpo.GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(id)).Estado);
        Assert.Equal(3, (await r.Reservas()).Count);
    }

    /// <summary>
    /// Primero se cobra y después se reserva: si la captura falla, ninguna reserva llegó a existir. Al revés, la agenda
    /// tendría reservas confirmadas de un viaje sin pagar, y deshacerlas dejaría su rastro de canceladas.
    /// </summary>
    [Fact]
    public async Task Si_la_captura_falla_ninguna_reserva_llega_a_confirmarse()
    {
        using var r = await new ViajesReservaReal(falla: m =>
            m.Method == HttpMethod.Post && m.RequestUri!.AbsolutePath.EndsWith("/capture", StringComparison.Ordinal)
                ? "payments.capture_failed"
                : null).ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, cerro);
        Assert.Equal("payments.capture_failed", cerrada.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal("Voided", (await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}")).GetProperty("status").GetString());
        Assert.Empty(await r.Reservas());
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Hotel, ViajesReservaReal.Salida.AddHours(5), ViajesReservaReal.Salida.AddDays(2)));
    }
}
