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
}
