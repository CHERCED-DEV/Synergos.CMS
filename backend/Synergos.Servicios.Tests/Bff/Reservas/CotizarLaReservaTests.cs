using System.Net;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Cotizar es la anticipación de abrir (011-booking-reserva, T025): corre la fase que abre hasta antes de apartar
/// y no guarda nada. El total que la pantalla muestra es el que abrir autoriza después.
/// </summary>
public sealed class CotizarLaReservaTests
{
    private static async Task<int> Apartados(ReservaDeServicioReal r)
    {
        var recurso = (await r.Leer("booking", $"v1/resources?subjectKind=reservas.servicio&subjectId={ReservaDeServicioReal.Servicio}"))
            .GetProperty("id").GetString();
        var desde = ReservaDeServicioReal.Inicio;
        var disponibilidad = await r.Leer("booking",
            $"v1/resources/{recurso}/availability?start={Uri.EscapeDataString(desde.ToString("O"))}&end={Uri.EscapeDataString(desde.AddMinutes(60).ToString("O"))}");
        return disponibilidad.GetProperty("taken").GetInt32();
    }

    [Fact]
    public async Task Cotizar_da_el_total_y_no_escribe_en_la_agenda_ni_en_pagos()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, cotizada) = await r.Cotizar();

        Assert.True(estado == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(140_000m, cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(0, await Apartados(r));
        // Sin saga no hay cobro que autorizar ni nada que deshacer: cotizar no guarda nada.
        Assert.Empty(r.Sagas.StartedBefore(DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Abrir_despues_autoriza_el_mismo_total_que_se_cotizo()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, cotizada) = await r.Cotizar();

        var (_, abierta) = await r.Abrir();

        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(abierta.GetProperty("id").GetString()!, "cobroId")}");
        Assert.Equal(
            cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal(),
            cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Sin_tarifa_cotizar_lo_dice_sin_apartar_nada()
    {
        using var r = await new ReservaDeServicioReal(falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/quotes$") ? "pricing.price_not_found" : null)
            .ConOferta();

        var (estado, cuerpo) = await r.Cotizar();

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await Apartados(r));
    }
}
