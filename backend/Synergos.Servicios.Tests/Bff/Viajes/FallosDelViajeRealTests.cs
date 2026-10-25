using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Historia 2 de 006-viajes-hoteles (T012): si algo falla a mitad, nada queda a medias, contra las capacidades reales; lo
/// que ninguna sabe hacer a pedido se inyecta en el cable.
/// </summary>
/// <remarks>
/// Cada ítem se deshace según en qué punto quedó: el apartado se suelta, la reserva ya confirmada se cancela; el cobro se
/// anula si no se capturó y se devuelve si sí. El rechazo que ve quien viaja es el ORIGINAL de la capacidad.
/// </remarks>
public sealed class FallosDelViajeRealTests
{
    private static string Id(JsonElement cuerpo) => cuerpo.GetProperty("id").GetString()!;

    private static bool Es(HttpRequestMessage r, string metodo, string sufijo)
        => r.Method.Method == metodo && r.RequestUri!.AbsolutePath.EndsWith(sufijo, StringComparison.Ordinal);

    private static async Task SinNadaTomado(ViajesReservaReal r)
    {
        var s = ViajesReservaReal.Salida;
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Vuelo, s, s.AddHours(2)));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Hotel, s.AddHours(5), s.AddDays(2)));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Auto, s.AddHours(2), s.AddDays(2)));
    }

    [Fact]
    public async Task Sin_cupo_en_el_tercer_item_se_sueltan_los_dos_primeros_sin_autorizar_y_se_dice_el_codigo_de_la_agenda()
    {
        using var r = new ViajesReservaReal();
        await r.ConProducto(ViajesReservaReal.Vuelo, 300_000m);
        await r.ConProducto(ViajesReservaReal.Hotel, 700_000m);
        await r.ConProducto(ViajesReservaReal.Auto, 200_000m, cupos: 1);
        var elAuto = ViajesReservaReal.ItinerarioDePrueba()[2];
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir(llave: "viaje-de-beto", viajero: "beto", items: [elAuto])).Estado);

        var (estado, cuerpo) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.at_capacity", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find("viaje-1")!.Status);
        Assert.False(r.Tiene("viaje-1", "cobroId"));
        var s = ViajesReservaReal.Salida;
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Vuelo, s, s.AddHours(2)));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Hotel, s.AddHours(5), s.AddDays(2)));
        Assert.Equal(1, await r.Ocupados(ViajesReservaReal.Auto, s.AddHours(2), s.AddDays(2)));
    }

    [Fact]
    public async Task Un_pago_rechazado_suelta_los_tres_apartados_y_devuelve_el_rechazo_original()
    {
        using var r = await new ViajesReservaReal(falla: m => Es(m, "POST", "/v1/payments") ? "payments.payment_declined" : null).ConLosTres();

        var (estado, cuerpo) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find("viaje-1")!.Status);
        await SinNadaTomado(r);
    }

    /// <summary>
    /// El último ítem no se confirma DESPUÉS de capturar: los dos ya confirmados se cancelan (soltarlos ya no sirve), el
    /// tercero se suelta, y el cobro se devuelve entero. Es el caso que el viaje imperativo resolvía con su compensación
    /// por ítem; el declarado lo hace con el cambio de deshacer de cada apartado al confirmarse.
    /// </summary>
    [Fact]
    public async Task Si_el_ultimo_no_se_confirma_tras_capturar_se_cancelan_los_confirmados_se_suelta_el_otro_y_se_devuelve_todo()
    {
        var confirmaciones = 0;
        using var r = await new ViajesReservaReal(falla: m =>
            Es(m, "POST", "/confirm") && Interlocked.Increment(ref confirmaciones) == 3 ? "booking.hold_expired" : null).ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.hold_expired", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal(["Cancelled", "Cancelled"], (await r.Reservas()).Select(x => x.Estado).ToArray());
        await SinNadaTomado(r);
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");
        Assert.Equal(1_200_000m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, cobro.GetProperty("refundable").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Un_apartado_vencido_en_el_primer_item_no_deja_ninguna_reserva_y_devuelve_todo()
    {
        using var r = await new ViajesReservaReal(falla: m => Es(m, "POST", "/confirm") ? "booking.hold_expired" : null).ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.hold_expired", cuerpo.GetProperty("code").GetString());
        Assert.Empty(await r.Reservas());
        await SinNadaTomado(r);
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");
        Assert.Equal(0m, cobro.GetProperty("refundable").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Abandonado_sin_cerrar_el_barrido_suelta_los_apartados_y_anula_el_cobro()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        r.Reloj.Avanzar(TimeSpan.FromMinutes(r.Abandono + 1));
        await r.UnaVuelta();

        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        await SinNadaTomado(r);
        Assert.Equal("Voided", (await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cerrado_y_treinta_dias_despues_el_barrido_no_lo_toca()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);
        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(id)).Estado);

        r.Reloj.Avanzar(TimeSpan.FromDays(30));
        await r.UnaVuelta();

        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(id)!.Status);
        Assert.All(await r.Reservas(), x => Assert.Equal("Confirmed", x.Estado));
        Assert.Equal("Captured", (await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Un_intento_deshecho_no_encierra_la_misma_intencion()
    {
        var rechazar = true;
        using var r = await new ViajesReservaReal(falla: m => rechazar && Es(m, "POST", "/v1/payments") ? "payments.payment_declined" : null)
            .ConLosTres();
        Assert.Equal(HttpStatusCode.Conflict, (await r.Abrir()).Estado);

        rechazar = false;
        var (estado, abierta) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.NotEqual("viaje-1", Id(abierta));
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
    }
}
