using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El cupón de la compra de entradas (ADR 0140, #201): del navegador llega el CÓDIGO, el descuento lo
/// calcula <c>Api.Pricing</c> al cotizar, y lo que se autoriza es el total que resulta.
/// </summary>
/// <remarks>
/// <para><b>Contra el <c>Program</c> real y las capacidades reales</b> (<see cref="CompraDeEventosReal"/>):
/// la promoción se crea en Pricing como la crearía quien opera el sitio, y lo que se mira es lo que
/// queda —el cobro autorizado en Payments, el aforo en Inventory, las sagas en el disco—, no lo que
/// contesta un doble.</para>
///
/// <para><b>La comisión sigue siendo sobre el subtotal de las entradas</b>, antes del descuento: es la
/// regla de la ADR 0137 que el carrito muestra y que cruzan los vectores de oro (G-12). Cambiarla sería
/// cambiarla en los tres sitios que la calculan.</para>
/// </remarks>
public sealed class CuponDeLaCompraTests
{
    private static readonly string A = CompraDeEventosReal.Sujeto("miembro-a");

    private static object Cuerpo(string? promotionCode)
        => new { eventId = "evt-1", lines = new[] { new { tier = "GEN", quantity = 2 } }, promotionCode };

    [Fact]
    public async Task Con_un_cupon_vigente_se_autoriza_el_total_que_descuenta_Pricing()
    {
        using var compra = new CompraDeEventosReal(precio: 100_000m);
        await compra.Promocion("BIENVENIDA10", "Percentage", 1000m);

        // El código como lo escribe una persona: en minúsculas y con un espacio pegado de un correo.
        var (abierta, cuerpo) = await compra.Abrir(A, CompraDeEventosReal.Negocio(12m), cuerpo: Cuerpo(" bienvenida10 "));

        Assert.True(abierta == HttpStatusCode.Created, $"{(int)abierta} {cuerpo}");
        // 2 × 100.000 = 200.000; Pricing descuenta el 10 % y quedan 180.000; la comisión del 12 % va
        // sobre el subtotal de las entradas, 200.000, como la muestra el carrito: 24.000. Total 204.000.
        Assert.Equal(204_000m, cuerpo.GetProperty("total").GetProperty("amount").GetDecimal());
        var id = cuerpo.GetProperty("id").GetString()!;
        var cobro = Assert.Single(await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
        Assert.Equal(204_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal("Authorized", cobro.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sin_cupon_el_total_es_el_de_siempre()
    {
        // El control del de arriba: con la misma promoción publicada y sin código, no se descuenta nada.
        using var compra = new CompraDeEventosReal(precio: 100_000m);
        await compra.Promocion("BIENVENIDA10", "Percentage", 1000m);

        var (abierta, cuerpo) = await compra.Abrir(A, CompraDeEventosReal.Negocio(12m), cuerpo: Cuerpo(promotionCode: "  "));

        Assert.True(abierta == HttpStatusCode.Created, $"{(int)abierta} {cuerpo}");
        Assert.Equal(224_000m, cuerpo.GetProperty("total").GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData("NOEXISTE", HttpStatusCode.NotFound, "pricing.promotion_not_found")]
    [InlineData("VENCIDO", HttpStatusCode.Gone, "pricing.promotion_expired")]
    public async Task Un_cupon_que_no_sirve_rechaza_con_el_codigo_de_Pricing_sin_apartar_ni_autorizar_nada(
        string codigo, HttpStatusCode esperado, string code)
    {
        using var compra = new CompraDeEventosReal();
        await compra.Promocion("VENCIDO", "Fixed", 5_000m, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        var (estado, rechazo) = await compra.Abrir(A, CompraDeEventosReal.Negocio(0m), cuerpo: Cuerpo(codigo));

        Assert.True(estado == esperado, $"{(int)estado} {rechazo}");
        Assert.Equal(code, rechazo.GetProperty("code").GetString());
        Assert.False(rechazo.GetProperty("transient").GetBoolean());

        // Cotizar va antes de apartar: el aforo está entero y la compra no dejó saga.
        var pozo = await compra.Leer("inventory", "v1/items?subjectKind=eventos.aforo&subjectId=evt-1%2FGEN");
        Assert.Equal(10, pozo.GetProperty("available").GetInt32());
        Assert.Equal(0, compra.SagasGuardadas);
    }

    [Fact]
    public async Task Tras_un_cupon_rechazado_la_misma_llave_compra_sin_el()
    {
        // La persona quita el código y vuelve a pulsar: el rechazo no dejó la llave ocupada.
        using var compra = new CompraDeEventosReal(precio: 100_000m);

        var (rechazada, _) = await compra.Abrir(A, CompraDeEventosReal.Negocio(0m), llave: "intencion-1", cuerpo: Cuerpo("NOEXISTE"));
        var (abierta, cuerpo) = await compra.Abrir(A, CompraDeEventosReal.Negocio(0m), llave: "intencion-1", cuerpo: Cuerpo(null));

        Assert.Equal(HttpStatusCode.NotFound, rechazada);
        Assert.True(abierta == HttpStatusCode.Created, $"{(int)abierta} {cuerpo}");
        Assert.Equal(200_000m, cuerpo.GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.String, cuerpo.GetProperty("id").ValueKind);
    }

    // ── Cotizar sin reservar: la vista previa honesta (la puerta la abre como «cotizar») ──

    private static decimal Monto(JsonElement cuerpo, string campo) => cuerpo.GetProperty(campo).GetProperty("amount").GetDecimal();

    [Fact]
    public async Task Cotizar_con_cupon_da_el_descuento_y_el_total_que_abrir_autoriza_sin_apartar_nada()
    {
        using var compra = new CompraDeEventosReal(precio: 100_000m);
        await compra.Promocion("BIENVENIDA10", "Percentage", 1000m);
        var negocio = CompraDeEventosReal.Negocio(12m);

        var (estado, cotizacion) = await compra.Cotizar(A, negocio, Cuerpo(" bienvenida10 "));

        Assert.True(estado == HttpStatusCode.OK, $"{(int)estado} {cotizacion}");
        Assert.Equal(200_000m, Monto(cotizacion, "subtotal"));
        Assert.Equal(20_000m, Monto(cotizacion, "discount"));
        Assert.Equal(0m, Monto(cotizacion, "tax"));
        Assert.Equal(24_000m, Monto(cotizacion, "serviceFee"));
        Assert.Equal(204_000m, Monto(cotizacion, "total"));
        Assert.Equal("COP", cotizacion.GetProperty("total").GetProperty("currency").GetString());
        Assert.Equal("bienvenida10", cotizacion.GetProperty("promotionCode").GetString());

        // Mirar no cuesta nada: el aforo entero, ninguna saga, y la cotización otra vez da lo mismo.
        var pozo = await compra.Leer("inventory", "v1/items?subjectKind=eventos.aforo&subjectId=evt-1%2FGEN");
        Assert.Equal(10, pozo.GetProperty("available").GetInt32());
        Assert.Equal(0, compra.SagasGuardadas);

        // Y lo que se vio es lo que se autoriza.
        var (abierta, cuerpo) = await compra.Abrir(A, negocio, cuerpo: Cuerpo("BIENVENIDA10"));
        Assert.True(abierta == HttpStatusCode.Created, $"{(int)abierta} {cuerpo}");
        Assert.Equal(Monto(cotizacion, "total"), Monto(cuerpo, "total"));
    }

    [Fact]
    public async Task Cotizar_sin_cupon_da_el_total_de_siempre_con_descuento_cero()
    {
        using var compra = new CompraDeEventosReal(precio: 100_000m);

        var (estado, cotizacion) = await compra.Cotizar(A, CompraDeEventosReal.Negocio(12m), Cuerpo(null));

        Assert.True(estado == HttpStatusCode.OK, $"{(int)estado} {cotizacion}");
        Assert.Equal(0m, Monto(cotizacion, "discount"));
        Assert.Equal(24_000m, Monto(cotizacion, "serviceFee"));
        Assert.Equal(224_000m, Monto(cotizacion, "total"));
        Assert.Equal(JsonValueKind.Null, cotizacion.GetProperty("promotionCode").ValueKind);
    }

    [Fact]
    public async Task Cotizar_un_cupon_que_no_existe_rechaza_con_el_codigo_de_Pricing_sin_guardar_nada()
    {
        using var compra = new CompraDeEventosReal();

        var (estado, rechazo) = await compra.Cotizar(A, CompraDeEventosReal.Negocio(0m), Cuerpo("NOEXISTE"));

        Assert.True(estado == HttpStatusCode.NotFound, $"{(int)estado} {rechazo}");
        Assert.Equal("pricing.promotion_not_found", rechazo.GetProperty("code").GetString());
        Assert.Equal(0, compra.SagasGuardadas);
    }

    // ── El tope de códigos que no sirven (ADR 0140, el cupón endurecido) ─────────────────────────
    //
    // «cotizar» —y «abrir» con un código que no sirve— contestan distinto a uno que no existe, a uno
    // vencido y a uno que vale: sin tope, un miembro enumeraba los códigos en bucle. Agotado el tope,
    // toda cotización o apertura CON código de ese comprador contesta eventos.demasiados_codigos,
    // transitorio, antes de llamar a Pricing; sin código se compra igual.

    private static readonly string B = CompraDeEventosReal.Sujeto("miembro-b");

    private static readonly Dictionary<string, string> TopeDeDos = new() { ["Eventos:Cupon:FallosPorVentana"] = "2" };

    private static void AssertCorte(HttpStatusCode estado, JsonElement rechazo)
    {
        Assert.True(estado == HttpStatusCode.ServiceUnavailable, $"{(int)estado} {rechazo}");
        Assert.Equal("eventos.demasiados_codigos", rechazo.GetProperty("code").GetString());
        Assert.True(rechazo.GetProperty("transient").GetBoolean());
    }

    [Fact]
    public async Task Agotado_el_tope_cotizar_con_codigo_se_corta_aunque_el_codigo_valga_y_sin_codigo_o_a_otro_no()
    {
        using var compra = new CompraDeEventosReal(precio: 100_000m, ajustes: TopeDeDos);
        await compra.Promocion("BIENVENIDA10", "Percentage", 1000m);
        var negocio = CompraDeEventosReal.Negocio(12m);
        foreach (var candidato in new[] { "PRUEBA1", "PRUEBA2" })
        {
            var (fallo, cuerpoDelFallo) = await compra.Cotizar(A, negocio, Cuerpo(candidato));
            Assert.True(fallo == HttpStatusCode.NotFound, $"{(int)fallo} {cuerpoDelFallo}");
        }

        // El corte no dice si el código existe: también se corta el que vale, antes de llegar a Pricing.
        var (estado, rechazo) = await compra.Cotizar(A, negocio, Cuerpo("BIENVENIDA10"));
        AssertCorte(estado, rechazo);

        var (sinCodigo, cotizacion) = await compra.Cotizar(A, negocio, Cuerpo(null));
        Assert.True(sinCodigo == HttpStatusCode.OK, $"{(int)sinCodigo} {cotizacion}");
        var (deOtro, suya) = await compra.Cotizar(B, negocio, Cuerpo("BIENVENIDA10"));
        Assert.True(deOtro == HttpStatusCode.OK, $"{(int)deOtro} {suya}");
        Assert.Equal(20_000m, Monto(suya, "discount"));
    }

    [Fact]
    public async Task Agotado_el_tope_por_abrir_abrir_con_codigo_no_aparta_ni_autoriza_y_sin_codigo_compra_igual()
    {
        using var compra = new CompraDeEventosReal(precio: 100_000m, ajustes: TopeDeDos);
        await compra.Promocion("BIENVENIDA10", "Percentage", 1000m);
        var negocio = CompraDeEventosReal.Negocio(12m);
        // «abrir» con un código que no sirve también es oráculo: los dos fallos, por abrir.
        foreach (var candidato in new[] { "PRUEBA1", "PRUEBA2" })
        {
            var (fallo, cuerpoDelFallo) = await compra.Abrir(A, negocio, llave: $"intento-{candidato}", cuerpo: Cuerpo(candidato));
            Assert.True(fallo == HttpStatusCode.NotFound, $"{(int)fallo} {cuerpoDelFallo}");
        }

        var (estado, rechazo) = await compra.Abrir(A, negocio, llave: "con-cupon", cuerpo: Cuerpo("BIENVENIDA10"));

        AssertCorte(estado, rechazo);
        Assert.Equal(0, compra.SagasGuardadas);
        var pozo = await compra.Leer("inventory", "v1/items?subjectKind=eventos.aforo&subjectId=evt-1%2FGEN");
        Assert.Equal(10, pozo.GetProperty("available").GetInt32());

        // Sin código, la compra de siempre: la semántica de los pagos no cambia.
        var (abierta, cuerpo) = await compra.Abrir(A, negocio, llave: "sin-cupon", cuerpo: Cuerpo(null));
        Assert.True(abierta == HttpStatusCode.Created, $"{(int)abierta} {cuerpo}");
        Assert.Equal(224_000m, Monto(cuerpo, "total"));
    }

    [Fact]
    public async Task Agotado_el_tope_la_llave_de_una_compra_viva_la_devuelve_igual()
    {
        // Reabrir con la misma llave no vuelve a cotizar (el orquestador devuelve su saga): no es oráculo,
        // y cortarlo dejaría a quien ya apartó sin poder reintentar su propia compra.
        using var compra = new CompraDeEventosReal(precio: 100_000m, ajustes: TopeDeDos);
        await compra.Promocion("BIENVENIDA10", "Percentage", 1000m);
        var negocio = CompraDeEventosReal.Negocio(12m);
        var (abierta, primera) = await compra.Abrir(A, negocio, llave: "intencion-1", cuerpo: Cuerpo("BIENVENIDA10"));
        Assert.True(abierta == HttpStatusCode.Created, $"{(int)abierta} {primera}");
        foreach (var candidato in new[] { "PRUEBA1", "PRUEBA2" })
        {
            await compra.Cotizar(A, negocio, Cuerpo(candidato));
        }

        var (otraVez, segunda) = await compra.Abrir(A, negocio, llave: "intencion-1", cuerpo: Cuerpo("BIENVENIDA10"));

        Assert.True(otraVez == HttpStatusCode.Created, $"{(int)otraVez} {segunda}");
        Assert.Equal(primera.GetProperty("id").GetString(), segunda.GetProperty("id").GetString());
        Assert.Equal(1, compra.SagasGuardadas);
    }
}
