using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Historia 2 de 011-booking-reserva: si algo falla a mitad, la franja vuelve a quedar libre y la plata vuelve,
/// contra las capacidades reales; lo que ninguna sabe hacer a pedido se inyecta en el cable.
/// </summary>
public sealed class FallosDeLaReservaTests
{
    [Fact]
    public async Task Sin_cupo_no_se_autoriza_nada_y_se_dice_que_esa_hora_ya_no_esta()
    {
        using var r = await new ReservaDeServicioReal().ConOferta(ReservaDeServicioReal.Oferta(cupos: 1));
        var (primera, _) = await r.Abrir(llave: "r-ana", cliente: "ana");
        Assert.Equal(HttpStatusCode.Created, primera);

        var (estado, cuerpo) = await r.Abrir(llave: "r-beto", cliente: "beto");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.at_capacity", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("r-beto", "cobroId"));
    }

    [Fact]
    public async Task Un_servicio_sin_tarifa_se_rechaza_sin_apartar_nada()
    {
        using var r = await new ReservaDeServicioReal(falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/quotes$") ? "pricing.price_not_found" : null)
            .ConOferta();

        var (estado, cuerpo) = await r.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("reserva-1", "apartadoId"));
    }

    [Fact]
    public async Task Un_pago_rechazado_suelta_la_franja_y_devuelve_el_rechazo_original()
    {
        using var r = await new ReservaDeServicioReal(falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/payments$") ? "payments.payment_declined" : null)
            .ConOferta();

        var (estado, cuerpo) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        var saga = r.Sagas.Find("reserva-1")!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.True((await r.Leer("booking", $"v1/holds/{r.Dato("reserva-1", "apartadoId")}")).GetProperty("released").GetBoolean());
    }

    [Fact]
    public async Task Una_captura_negada_anula_la_autorizacion_y_suelta_la_franja()
    {
        using var r = await new ReservaDeServicioReal(falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/payments/[^/]+/capture$") ? "payments.capture_failed" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.capture_failed", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal("Voided", (await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}")).GetProperty("status").GetString());
        Assert.True((await r.Leer("booking", $"v1/holds/{r.Dato(id, "apartadoId")}")).GetProperty("released").GetBoolean());
    }

    /// <summary>
    /// El apartado vencido (o cualquier confirmación negada) llega DESPUÉS de capturar: se devuelve entero. La
    /// pantalla no puede decir «no se te cobró» (spec, historia 2 escenario 5, arreglo I1 del análisis).
    /// </summary>
    [Fact]
    public async Task Si_la_agenda_no_confirma_tras_capturar_se_devuelve_el_cobro_entero_y_la_franja_queda_libre()
    {
        using var r = await new ReservaDeServicioReal(falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/holds/[^/]+/confirm$") ? "booking.hold_expired" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.hold_expired", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");
        Assert.Equal(0m, cobro.GetProperty("refundable").GetProperty("amount").GetDecimal());
        Assert.True((await r.Leer("booking", $"v1/holds/{r.Dato(id, "apartadoId")}")).GetProperty("released").GetBoolean());
        Assert.Equal(0, await r.Avisos());
    }

    [Fact]
    public async Task Un_intento_deshecho_no_encierra_la_misma_intencion()
    {
        var rechazar = true;
        using var r = await new ReservaDeServicioReal(falla: p => rechazar && ReservaDeServicioReal.Es(p, "POST", "^/v1/payments$") ? "payments.payment_declined" : null)
            .ConOferta();
        Assert.Equal(HttpStatusCode.Conflict, (await r.Abrir()).Estado);

        rechazar = false;
        var (estado, abierta) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.NotEqual("reserva-1", abierta.GetProperty("id").GetString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
    }

    /// <summary>T020: una reserva abandonada se barre a los 15 minutos (como Salud), no a los 60 por defecto.</summary>
    [Fact]
    public void El_abandono_se_barre_a_los_15_minutos_salvo_que_el_despliegue_diga_otra_cosa()
    {
        using var r = new ReservaDeServicioReal();
        Assert.Equal(15, r.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);

        using var otro = new ReservaDeServicioReal(ajustes: new Dictionary<string, string> { ["reservas:Sweep:AbandonAfterMinutes"] = "30" });
        Assert.Equal(30, otro.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);
    }
}
