using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// El alojamiento se cotiza por noche (lote 10, 006-H2), contra las capacidades reales: la tarifa por noche por las
/// noches de la estadía, el mismo total al cotizar que al abrir, y la marca que no deja pagar una noche por tres.
/// </summary>
public sealed class TarifaPorNocheTests
{
    private const string Estandar = "STD/STD-FLEX";
    private static readonly DateTimeOffset Entrada = new(DateTimeOffset.UtcNow.Date.AddDays(30), TimeSpan.Zero);

    private static decimal Total(JsonElement cuerpo)
        => cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    [Fact]
    public async Task Tres_noches_cuestan_tres_noches_y_lo_que_se_cotiza_es_lo_que_abrir_autoriza()
    {
        using var r = await new ViajesReservaReal().ConAlojamiento(Estandar, 220_000m);
        object[] estadia = [ViajesReservaReal.ItemPorNoche(Estandar, Entrada, Entrada.AddDays(3), "Habitación Estándar")];

        var (_, cotizado) = await r.Cotizar(items: estadia);
        var (estado, abierto) = await r.Abrir(items: estadia);

        Assert.True(estado == HttpStatusCode.Created, abierto.ToString());
        // Lo que dice la oferta del CMS para esas tres noches: 220.000 × 3.
        Assert.Equal(660_000m, Total(cotizado));
        Assert.Equal(660_000m, Total(abierto));
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(abierto.GetProperty("id").GetString()!, "cobroId")}");
        Assert.Equal(660_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Un_alojamiento_marcado_por_producto_no_se_abarata_se_rechaza_sin_apartar_ni_cobrar()
    {
        using var r = await new ViajesReservaReal().ConAlojamiento(Estandar, 220_000m);

        var (estado, cuerpo) = await r.Abrir(items: [ViajesReservaReal.ItemPorNoche(Estandar, Entrada, Entrada.AddDays(3), porNoche: false)]);

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, r.Escrituras("booking"));
        Assert.Equal(0, r.Escrituras("payments"));
    }

    [Fact]
    public async Task Un_vuelo_marcado_por_noche_no_tiene_tarifa_y_se_rechaza()
    {
        using var r = await new ViajesReservaReal().ConProducto(ViajesReservaReal.Vuelo, 300_000m);

        var (estado, cuerpo) = await r.Cotizar(items: [ViajesReservaReal.ItemPorNoche(ViajesReservaReal.Vuelo, Entrada, Entrada.AddDays(2))]);

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString());
    }
}
