using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El cupón de la compra de la tienda lo calcula y lo cobra el servidor (004-tienda-compra, US 8, FR-027): «cotizar»
/// dice el descuento sin apartar ni cobrar, «abrir» autoriza eso mismo, un código que no sirve rechaza antes de
/// apartar, y los que no sirven se frenan por comprador.
/// </summary>
/// <remarks>
/// Contra Pricing, Inventory y Payments reales (<see cref="CompraDeTiendaReal"/>): la promoción la crea quien opera
/// el sitio en Pricing, y del cuerpo viaja sólo el código. La canasta de siempre suma 95.000; con el 10 %, 85.500.
/// </remarks>
public sealed class CuponDeLaTiendaTests
{
    private static readonly DateTimeOffset Ayer = DateTimeOffset.UtcNow.AddDays(-1);

    private static async Task<CompraDeTiendaReal> ConPromociones(int? tope = null)
    {
        var t = new CompraDeTiendaReal(ajustes: tope is { } n
            ? new Dictionary<string, string> { ["Tienda:Cupon:FallosPorVentana"] = n.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            : null);
        await t.Promocion("DIEZ", "Percentage", 1000m, Ayer, DateTimeOffset.UtcNow.AddDays(30));
        await t.Promocion("VENCIDO", "Percentage", 5000m, Ayer.AddDays(-30), Ayer);
        return t;
    }

    private static decimal Monto(JsonElement cuerpo, string dato)
        => cuerpo.GetProperty("datos").GetProperty(dato).GetProperty("amount").GetDecimal();

    private static string? Codigo(JsonElement cuerpo) => cuerpo.ValueKind == JsonValueKind.Object ? cuerpo.GetProperty("code").GetString() : null;

    private static bool Aparta(string llamada) => llamada.EndsWith("/holds", StringComparison.Ordinal);

    private static bool Cobra(string llamada) => llamada == "POST /v1/payments";

    [Fact]
    public async Task Con_un_codigo_vigente_cotizar_dice_el_descuento_sin_apartar_ni_cobrar_y_abrir_autoriza_eso_mismo()
    {
        using var t = await ConPromociones();

        var (cotizo, cotizada) = await t.Cotizar(promocion: "DIEZ");

        Assert.True(cotizo == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(95_000m, Monto(cotizada, "subtotal"));
        Assert.Equal(9_500m, Monto(cotizada, "descuento"));
        Assert.Equal(85_500m, Monto(cotizada, "total"));
        Assert.DoesNotContain(t.Llamadas, l => Aparta(l) || Cobra(l));

        var (abrio, abierta) = await t.Abrir(promocion: "DIEZ");

        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal(85_500m, Monto(abierta, "total"));
        var cobro = await t.Leer("payments", $"v1/payments/{t.Dato(abierta.GetProperty("id").GetString()!, "cobroId")}");
        Assert.Equal(85_500m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Un_codigo_con_espacios_alrededor_es_el_mismo_codigo_y_uno_en_blanco_es_comprar_sin_codigo()
    {
        using var t = await ConPromociones();

        var (cotizo, cotizada) = await t.Cotizar(promocion: "  diez  ");
        Assert.True(cotizo == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(85_500m, Monto(cotizada, "total"));

        var (sinCodigo, sinDescuento) = await t.Cotizar(promocion: "   ");
        Assert.True(sinCodigo == HttpStatusCode.OK, sinDescuento.ToString());
        Assert.Equal(95_000m, Monto(sinDescuento, "total"));
    }

    [Fact]
    public async Task Un_codigo_que_no_existe_o_vencido_rechaza_sin_apartar_ni_cobrar_nada()
    {
        using var t = await ConPromociones();

        var (cotizo, rechazo) = await t.Cotizar(promocion: "NO-EXISTE");
        Assert.NotEqual(HttpStatusCode.OK, cotizo);
        Assert.Equal("pricing.promotion_not_found", Codigo(rechazo));

        var (abrio, vencido) = await t.Abrir(promocion: "VENCIDO");
        Assert.NotEqual(HttpStatusCode.Created, abrio);
        Assert.Equal("pricing.promotion_expired", Codigo(vencido));

        Assert.DoesNotContain(t.Llamadas, l => Aparta(l) || Cobra(l));
        Assert.Equal(10, await t.Disponibles("p-1"));
    }

    [Fact]
    public async Task Agotado_el_tope_con_codigo_se_corta_antes_de_cotizar_y_sin_codigo_o_con_otro_comprador_se_sigue()
    {
        using var t = await ConPromociones(tope: 2);
        Assert.Equal("pricing.promotion_not_found", Codigo((await t.Cotizar(promocion: "NOPE-1")).Cuerpo));
        Assert.Equal("pricing.promotion_not_found", Codigo((await t.Cotizar(promocion: "NOPE-2")).Cuerpo));
        var cotizaciones = t.Veces("POST /v1/quotes");

        var (cortada, corte) = await t.Cotizar(promocion: "DIEZ");
        var (abrir, corteAlAbrir) = await t.Abrir(promocion: "DIEZ", llave: "compra-cortada");

        Assert.Equal(HttpStatusCode.Conflict, cortada);
        Assert.Equal("tienda.demasiados_codigos", Codigo(corte));
        Assert.False(corte.GetProperty("transient").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, abrir);
        Assert.Equal("tienda.demasiados_codigos", Codigo(corteAlAbrir));
        Assert.Equal(cotizaciones, t.Veces("POST /v1/quotes"));
        Assert.DoesNotContain(t.Llamadas, l => Aparta(l) || Cobra(l));

        Assert.Equal(HttpStatusCode.OK, (await t.Cotizar()).Estado);
        Assert.Equal(HttpStatusCode.OK, (await t.Cotizar(promocion: "DIEZ", sujeto: "u-2")).Estado);
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir(llave: "compra-sin-codigo")).Estado);
    }

    [Fact]
    public async Task Un_codigo_que_vale_no_cuenta_para_el_tope()
    {
        using var t = await ConPromociones(tope: 1);

        for (var i = 0; i < 3; i++)
        {
            var (estado, cuerpo) = await t.Cotizar(promocion: "DIEZ");
            Assert.True(estado == HttpStatusCode.OK, $"vez {i + 1}: {(int)estado} {cuerpo}");
        }
    }

    [Fact]
    public async Task Una_apertura_que_el_orquestador_devolveria_tal_cual_no_se_corta()
    {
        using var t = await ConPromociones(tope: 1);
        var (abrio, abierta) = await t.Abrir(promocion: "DIEZ", llave: "compra-con-cupon");
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("pricing.promotion_not_found", Codigo((await t.Cotizar(promocion: "NOPE")).Cuerpo));

        var (otraVez, misma) = await t.Abrir(promocion: "DIEZ", llave: "compra-con-cupon");
        var (nueva, cortada) = await t.Abrir(promocion: "DIEZ", llave: "compra-nueva");

        Assert.True(otraVez != HttpStatusCode.Conflict, misma.ToString());
        Assert.Equal(abierta.GetProperty("id").GetString(), misma.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Conflict, nueva);
        Assert.Equal("tienda.demasiados_codigos", Codigo(cortada));
    }
}
