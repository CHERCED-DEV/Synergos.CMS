using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// El vuelo se cotiza por trayecto y por pasajero, y el auto por día (lote 13, 006-P9), contra las capacidades reales: lo
/// que anuncia la búsqueda del catálogo es lo que se cotiza, lo que abrir autoriza y lo que se cobra, para cualquier
/// fecha y pasajeros; y una marca mal puesta no abarata nada.
/// </summary>
public sealed class TarifaPorTrayectoYPorDiaTests
{
    private const string Vuelo = "SYN2200-ECOBAS";
    private const string Auto = "ECON";
    private static readonly DateTimeOffset Ida = new(DateTimeOffset.UtcNow.Date.AddDays(30), TimeSpan.Zero);

    private static decimal Total(JsonElement cuerpo)
        => cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    private static decimal PrecioDe(JsonElement cuerpo, int item)
        => cuerpo.GetProperty("datos").GetProperty("itinerario")[item].GetProperty("precio").GetProperty("amount").GetDecimal();

    [Fact] // happy: el caso que pidió verificar el director
    public async Task Ida_y_vuelta_para_dos_y_un_auto_de_tres_dias_cuestan_lo_que_anuncia_el_catalogo_y_eso_se_cobra()
    {
        using var r = await new ViajesReservaReal().ConVuelo(Vuelo, 340_000m, 255_000m);
        await r.ConAuto(Auto, 95_000m);
        object[] viaje =
        [
            ViajesReservaReal.ItemDeVuelo(Vuelo, Ida, Ida.AddDays(3), tramos: 2, adultos: 2, etiqueta: "BOG → CTG"),
            ViajesReservaReal.ItemPorDia(Auto, Ida, Ida.AddDays(3), "Económico"),
        ];

        var (_, cotizado) = await r.Cotizar(items: viaje);
        var (estado, abierto) = await r.Abrir(items: viaje);

        Assert.True(estado == HttpStatusCode.Created, abierto.ToString());
        // El catálogo: 340.000 por adulto y tramo × 2 adultos × 2 tramos, y 95.000 × 3 días.
        Assert.Equal(1_360_000m + 285_000m, Total(cotizado));
        Assert.Equal(1_360_000m + 285_000m, Total(abierto));
        Assert.Equal(1_360_000m, PrecioDe(abierto, 0));
        Assert.Equal(285_000m, PrecioDe(abierto, 1));
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(abierto.GetProperty("id").GetString()!, "cobroId")}");
        Assert.Equal(1_645_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact] // happy: el niño con su tarifa; el itinerario dice cómo se cobró
    public async Task Un_nino_paga_su_tarifa_de_nino_y_el_itinerario_dice_tramos_y_pasajeros()
    {
        using var r = await new ViajesReservaReal().ConVuelo(Vuelo, 340_000m, 255_000m);

        var (_, cotizado) = await r.Cotizar(items: [ViajesReservaReal.ItemDeVuelo(Vuelo, Ida, Ida.AddDays(3), tramos: 2, adultos: 2, ninos: 1)]);

        // 340.000 × 4 trayectos de adulto + 255.000 × 2 de niño.
        Assert.Equal(1_870_000m, Total(cotizado));
        Assert.Equal(1_870_000m, PrecioDe(cotizado, 0));
        var parada = cotizado.GetProperty("datos").GetProperty("itinerario")[0];
        Assert.Equal("trayecto", parada.GetProperty("unidad").GetString());
        Assert.Equal((2, 2, 1), (parada.GetProperty("tramos").GetInt32(), parada.GetProperty("adultos").GetInt32(), parada.GetProperty("ninos").GetInt32()));
    }

    [Fact] // filter: sin la marca el vuelo no encuentra tarifa: nunca se cobra un trayecto por dos
    public async Task Un_vuelo_sin_sus_tramos_no_se_abarata_se_rechaza_sin_apartar_ni_cobrar()
    {
        using var r = await new ViajesReservaReal().ConVuelo(Vuelo, 340_000m);

        var (estado, cuerpo) = await r.Abrir(items: [ViajesReservaReal.Item(Vuelo, Ida, Ida.AddDays(3))]);

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, r.Escrituras("booking"));
        Assert.Equal(0, r.Escrituras("payments"));
    }

    [Fact] // filter: un niño sin tarifa de niño no viaja gratis
    public async Task Un_nino_sin_tarifa_de_nino_se_rechaza_en_vez_de_viajar_gratis()
    {
        using var r = await new ViajesReservaReal().ConVuelo(Vuelo, 340_000m);

        var (estado, cuerpo) = await r.Cotizar(items: [ViajesReservaReal.ItemDeVuelo(Vuelo, Ida, Ida.AddDays(3), tramos: 2, adultos: 1, ninos: 1)]);

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString());
    }

    [Fact] // filter: un auto marcado por noche no tiene tarifa
    public async Task Un_auto_marcado_por_noche_no_tiene_tarifa_y_se_rechaza()
    {
        using var r = await new ViajesReservaReal().ConAuto(Auto, 95_000m);

        var (estado, cuerpo) = await r.Cotizar(items: [ViajesReservaReal.ItemPorNoche(Auto, Ida, Ida.AddDays(3))]);

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString());
    }

    [Fact] // empty: una marca que no es de ese ítem se rechaza antes de cotizar
    public async Task Pasajeros_en_un_auto_se_rechazan_antes_de_tocar_nada()
    {
        using var r = await new ViajesReservaReal().ConAuto(Auto, 95_000m);
        object auto = new { producto = Auto, ventana = new { start = Ida, end = Ida.AddDays(3) }, porDia = true, adultos = 2 };

        var (estado, cuerpo) = await r.Abrir(items: [auto]);

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("viajes.bad_fare_mark", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, r.Escrituras("booking"));
        Assert.Equal(0, r.Escrituras("payments"));
    }
}
