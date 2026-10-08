using System.Net;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Una compra de entradas es de quien la abrió, y eso lo comprueba el orquestador (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>En el orquestador y no sólo en la puerta</b>: con la regla sólo en la puerta, un fallo de
/// la puerta reabre el IDOR. Quién compra lo dice la puerta en <c>X-Synergos-Sujeto</c>, desde la
/// sesión; el navegador no puede escribirlo. Y la comisión, en <c>X-Synergos-Negocio</c>, desde la
/// configuración del sitio: el navegador no puede fijarse un 0 %.</para>
///
/// <para>Contra el <c>Program</c> real y las capacidades reales (<see cref="CompraDeEventosReal"/>):
/// lo que se cobra lo cotiza Pricing, lo que se aparta lo aparta Inventory.</para>
/// </remarks>
public sealed class DuenoDeLaCompraTests
{
    private static readonly string A = CompraDeEventosReal.Sujeto("miembro-a");
    private static readonly string B = CompraDeEventosReal.Sujeto("miembro-b");
    private static readonly string SinComision = CompraDeEventosReal.Negocio(0m);

    [Fact]
    public async Task B_no_lee_ni_confirma_ni_cancela_la_compra_de_A()
    {
        using var compra = new CompraDeEventosReal();
        var (abierta, deA) = await compra.Abrir(A, SinComision);
        Assert.Equal(HttpStatusCode.Created, abierta);
        var id = deA.GetProperty("id").GetString()!;

        foreach (var accion in new string?[] { null, "confirm", "cancel" })
        {
            var (estado, cuerpo) = await compra.Sobre(id, accion, B);
            Assert.True(estado == HttpStatusCode.NotFound, $"{accion ?? "GET"} de B sobre la compra de A: {(int)estado} {cuerpo}");
            Assert.Equal("eventos.purchase_not_found", cuerpo.GetProperty("code").GetString());
        }

        // Control: A sí la ve, y sigue en curso — nada de lo que intentó B la tocó.
        var (deSuDueno, vista) = await compra.Sobre(id, null, A);
        Assert.Equal(HttpStatusCode.OK, deSuDueno);
        Assert.Equal("Running", vista.GetProperty("status").GetString());
        Assert.Equal("Authorized", Assert.Single(await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={id}"))
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task B_con_la_misma_llave_que_A_abre_OTRA_compra_y_su_reintento_cae_en_la_suya()
    {
        using var compra = new CompraDeEventosReal();
        var (_, deA) = await compra.Abrir(A, SinComision, llave: "llave-comun");

        var (abiertaB, deB) = await compra.Abrir(B, SinComision, llave: "llave-comun");
        var (otraVezB, deBOtraVez) = await compra.Abrir(B, SinComision, llave: "llave-comun");

        Assert.Equal(HttpStatusCode.Created, abiertaB);
        Assert.NotEqual(deA.GetProperty("id").GetString(), deB.GetProperty("id").GetString());
        Assert.Equal("miembro-b", deB.GetProperty("buyerId").GetString());
        // La idempotencia de B no se pierde: su reintento es SU compra, no una tercera.
        Assert.Equal(HttpStatusCode.Created, otraVezB);
        Assert.Equal(deB.GetProperty("id").GetString(), deBOtraVez.GetProperty("id").GetString());
        Assert.Equal("miembro-a", (await compra.Sobre(deA.GetProperty("id").GetString()!, null, A)).Cuerpo
            .GetProperty("buyerId").GetString());
    }

    [Fact]
    public async Task Un_comprador_o_una_comision_en_el_cuerpo_se_ignoran()
    {
        using var compra = new CompraDeEventosReal(precio: 100_000m);

        var (abierta, cuerpo) = await compra.Abrir(A, CompraDeEventosReal.Negocio(12m), cuerpo: new
        {
            eventId = "evt-1",
            buyerKind = "eventos.comprador",
            buyerId = "victima",
            serviceFeePercent = 0m,
            lines = new[] { new { tier = "GEN", quantity = 2 } },
        });

        Assert.Equal(HttpStatusCode.Created, abierta);
        Assert.Equal("miembro-a", cuerpo.GetProperty("buyerId").GetString());
        // 2 × 100.000 cotizados por Pricing, más el 12 % que pone la puerta; el 0 del cuerpo no cuenta.
        Assert.Equal(224_000m, cuerpo.GetProperty("total").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Sin_sujeto_no_se_abre_ni_se_mira_nada_y_sin_negocio_no_se_abre()
    {
        using var compra = new CompraDeEventosReal();

        var (sinSujeto, rechazo) = await compra.Abrir(sujeto: null, SinComision);
        Assert.Equal(HttpStatusCode.BadRequest, sinSujeto);
        Assert.Equal("eventos.sujeto_requerido", rechazo.GetProperty("code").GetString());

        // Sin comisión NO es «comisión 0»: es no saber cuánto cobrar, y no se cobra.
        var (sinNegocio, otro) = await compra.Abrir(A, negocio: null);
        Assert.Equal(HttpStatusCode.BadRequest, sinNegocio);
        Assert.Equal("eventos.negocio_requerido", otro.GetProperty("code").GetString());

        var (_, deA) = await compra.Abrir(A, SinComision);
        var id = deA.GetProperty("id").GetString()!;
        foreach (var accion in new string?[] { null, "confirm", "cancel" })
        {
            var (estado, cuerpo) = await compra.Sobre(id, accion, sujeto: null);
            Assert.True(estado == HttpStatusCode.BadRequest, $"{accion ?? "GET"} sin sujeto: {(int)estado} {cuerpo}");
            Assert.Equal("eventos.sujeto_requerido", cuerpo.GetProperty("code").GetString());
        }
    }
}
