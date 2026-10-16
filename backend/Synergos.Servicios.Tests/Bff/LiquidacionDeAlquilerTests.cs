using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La liquidación del alquiler de la puerta (línea 005, T017; decisión 5 del lote 1, #203): el inquilino declara
/// sin mover plata y el personal confirma con el daño, contra Booking, Pricing y Payments reales.
/// </summary>
/// <remarks>
/// Un andamio por siete días con una garantía de 400 000: el daño de 120 000 deja 280 000 por devolver, y uno de
/// 500 000 no cabe. Con el daño igual a la garantía, «capturar y devolver el sobrante» y «capturar y ya» darían lo
/// mismo y la rama del sobrante pasaría en verde sin ejecutarse.
/// </remarks>
public sealed class LiquidacionDeAlquilerTests
{
    private static async Task<(RentaDeAlquilerReal Renta, string Id, string Garantia)> Rentado(Func<HttpRequestMessage, string?>? falla = null)
    {
        var r = new RentaDeAlquilerReal(falla);
        Assert.Equal(HttpStatusCode.OK, (await r.PublicarOferta(garantia: 400_000m)).Estado);
        var (estado, cuerpo) = await r.Rentar(pedido: RentaDeAlquilerReal.Pedido(unidades: 1));
        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        return (r, cuerpo.GetProperty("id").GetString()!, cuerpo.GetProperty("datos").GetProperty("garantiaId").GetString()!);
    }

    private static async Task<JsonElement> Cobro(RentaDeAlquilerReal r, string id) => await r.Leer("payments", $"v1/payments/{id}");

    private static string Codigo(JsonElement problema) => problema.GetProperty("code").GetString()!;

    [Fact]
    public async Task Declarar_no_mueve_plata_y_la_garantia_sigue_retenida()
    {
        var (r, id, garantia) = await Rentado();
        using var _ = r;
        var antes = r.Cable.Llamadas.Count;

        var (estado, cuerpo) = await r.Declarar(id);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal("declared", cuerpo.GetProperty("state").GetString());
        Assert.Equal(400_000m, cuerpo.GetProperty("depositHeld").GetDecimal());
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
        Assert.Equal("Authorized", (await Cobro(r, garantia)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Confirmar_sin_danio_anula_la_garantia()
    {
        var (r, id, garantia) = await Rentado();
        using var _ = r;
        Assert.Equal(HttpStatusCode.OK, (await r.Declarar(id)).Estado);

        var (estado, cuerpo) = await r.Confirmar(id, 0m);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal("returned", cuerpo.GetProperty("state").GetString());
        Assert.Equal(0m, cuerpo.GetProperty("depositHeld").GetDecimal());
        Assert.Equal(400_000m, cuerpo.GetProperty("depositReleased").GetDecimal());
        Assert.Equal("Voided", (await Cobro(r, garantia)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Con_danio_captura_una_vez_y_devuelve_el_sobrante()
    {
        var (r, id, garantia) = await Rentado();
        using var _ = r;

        var (estado, cuerpo) = await r.Confirmar(id, 120_000m);

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(120_000m, cuerpo.GetProperty("damageCharged").GetDecimal());
        Assert.Equal(280_000m, cuerpo.GetProperty("depositReleased").GetDecimal());
        var cobro = await Cobro(r, garantia);
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());
        Assert.Equal(120_000m, cobro.GetProperty("refundable").GetProperty("amount").GetDecimal());
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{garantia}/capture$"));
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{garantia}/refund$"));
    }

    [Fact]
    public async Task Un_danio_mayor_que_la_garantia_se_rechaza_sin_mover_nada()
    {
        var (r, id, garantia) = await Rentado();
        using var _ = r;
        var antes = r.Cable.Llamadas.Count;

        var (estado, cuerpo) = await r.Confirmar(id, 500_000m);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("alquiler.damage_exceeds_deposit", Codigo(cuerpo));
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
        Assert.Equal("Authorized", (await Cobro(r, garantia)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task El_personal_confirma_sin_declaracion_y_repetir_no_captura_otra_vez()
    {
        var (r, id, garantia) = await Rentado();
        using var _ = r;

        var (primera, cuerpo) = await r.Confirmar(id, 120_000m);
        var (segunda, otra) = await r.Confirmar(id, 120_000m);

        Assert.Equal(HttpStatusCode.OK, primera);
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("declaredAtUtc").ValueKind);
        Assert.Equal(HttpStatusCode.OK, segunda);
        Assert.Equal("returned", otra.GetProperty("state").GetString());
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{garantia}/capture$"));
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{garantia}/refund$"));
    }

    [Fact]
    public async Task Dos_confirmaciones_en_carrera_capturan_una_vez()
    {
        var (r, id, garantia) = await Rentado();
        using var _ = r;

        var resultados = await Task.WhenAll(r.Confirmar(id, 120_000m), r.Confirmar(id, 120_000m));

        Assert.Contains(resultados, x => x.Estado == HttpStatusCode.OK);
        Assert.All(resultados, x => Assert.True(x.Estado is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable, x.Cuerpo.ToString()));
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{garantia}/capture$"));
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{garantia}/refund$"));
    }

    [Fact]
    public async Task Un_alquiler_ajeno_no_existe_y_lo_devuelto_ya_no_se_declara()
    {
        var (r, id, _) = await Rentado();
        using var __ = r;

        var (ajeno, rechazo) = await r.Declarar(id, sujeto: RentaDeAlquilerReal.Sujeto("u-2"));
        Assert.Equal(HttpStatusCode.NotFound, ajeno);
        Assert.Equal("alquiler.saga_not_found", Codigo(rechazo));

        Assert.Equal(HttpStatusCode.OK, (await r.Confirmar(id, 0m)).Estado);
        var (tarde, otro) = await r.Declarar(id);
        Assert.Equal(HttpStatusCode.Conflict, tarde);
        Assert.Equal("alquiler.not_declarable", Codigo(otro));
    }

    [Fact]
    public async Task Un_alquiler_sin_confirmar_no_se_declara_ni_se_confirma()
    {
        using var r = new RentaDeAlquilerReal();
        Assert.Equal(HttpStatusCode.OK, (await r.PublicarOferta(garantia: 400_000m)).Estado);
        var (_, abierto) = await r.Abrir(pedido: RentaDeAlquilerReal.Pedido(unidades: 1));
        var id = abierto.GetProperty("id").GetString()!;

        Assert.Equal("alquiler.not_declarable", Codigo((await r.Declarar(id)).Cuerpo));
        Assert.Equal("alquiler.not_returnable", Codigo((await r.Confirmar(id, 0m)).Cuerpo));
    }

    [Fact]
    public async Task Con_la_retencion_vencida_sin_danio_cuenta_liberada_y_con_danio_se_rechaza()
    {
        // La pasarela ya no retiene: se anula fuera del orquestador, como haría un vencimiento.
        var (r, id, garantia) = await Rentado();
        using var _ = r;
        using (var pagos = r.Capacidades.CreateClient("payments"))
        using (var anular = new HttpRequestMessage(HttpMethod.Post, $"v1/payments/{garantia}/void"))
        {
            using var anulada = await pagos.SendAsync(anular);
            Assert.True(anulada.IsSuccessStatusCode);
        }

        var (conDanio, rechazo) = await r.Confirmar(id, 120_000m);
        Assert.Equal(HttpStatusCode.Conflict, conDanio);
        Assert.Equal("alquiler.retention_expired", Codigo(rechazo));

        var (sinDanio, cuerpo) = await r.Confirmar(id, 0m);
        Assert.Equal(HttpStatusCode.OK, sinDanio);
        Assert.Equal(400_000m, cuerpo.GetProperty("depositReleased").GetDecimal());
    }
}
