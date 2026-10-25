using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Cotizar es la anticipación de abrir (006-viajes-hoteles, T024): la pone el host, corre la fase que abre hasta el
/// bloque que aparta y no guarda nada. El total que la pantalla muestra antes de reservar es el que abrir autoriza.
/// </summary>
public sealed class CotizarElViajeTests
{
    private static decimal Total(JsonElement cuerpo)
        => cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    [Fact]
    public async Task Cotizar_da_el_total_y_no_escribe_en_la_agenda_ni_en_pagos()
    {
        using var r = await new ViajesReservaReal().ConLosTres();

        var (estado, cotizado) = await r.Cotizar();

        Assert.True(estado == HttpStatusCode.OK, cotizado.ToString());
        Assert.Equal(1_200_000m, Total(cotizado));
        Assert.Equal(0, r.Escrituras("booking"));
        Assert.Equal(0, r.Escrituras("payments"));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Hotel, ViajesReservaReal.Salida, ViajesReservaReal.Salida.AddDays(2)));
        // Sin saga no hay cobro que autorizar ni nada que deshacer: cotizar no guarda nada.
        Assert.Empty(r.Sagas.StartedBefore(DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Abrir_despues_autoriza_el_mismo_total_que_se_cotizo()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, cotizado) = await r.Cotizar();

        var (estado, abierto) = await r.Abrir();

        Assert.True(estado == HttpStatusCode.Created, abierto.ToString());
        Assert.Equal(Total(cotizado), Total(abierto));
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(abierto.GetProperty("id").GetString()!, "cobroId")}");
        Assert.Equal(Total(cotizado), cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Sin_tarifa_cotizar_lo_dice_sin_apartar_nada()
    {
        using var r = await new ViajesReservaReal().ConProducto(ViajesReservaReal.Vuelo, 300_000m);
        await r.ConProducto(ViajesReservaReal.Hotel, 700_000m);

        var (estado, cuerpo) = await r.Cotizar();

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, r.Escrituras("booking"));
        Assert.Equal(0, r.Escrituras("payments"));
    }
}
