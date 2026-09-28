using System.Net.Http.Json;
using System.Text.Json;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El cobro de un orquestador, contra <c>Api.Payments</c> DE VERDAD (#168, con el arnés del #162).
/// </summary>
/// <remarks>
/// <para><b>Esto es lo que ningún doble podía decir.</b> Los seis <c>CapacidadesFalsas</c>
/// contestan 200 a lo que sea, así que el flujo entero pasaba en verde mientras la capacidad real
/// rechazaba <b>todos</b> los cobros con <c>payments.access_requires_identity</c>: ningún
/// orquestador declara afirmación de identidad, y por diseño no puede —propagar el token del CMS a
/// través de una saga está descartado (un token es una credencial CON RELOJ, una saga es trabajo
/// CON DURACIÓN)—.</para>
///
/// <para><b>La mutación de este test es el defecto</b>: volver a exigir la afirmación en
/// <c>PaymentEndpoints.Afirmacion</c> lo pone rojo con el 400 exacto.</para>
///
/// <para><b>Y el segundo caso NO es decoración.</b> Ausente es «no consta»; presente e ILEGIBLE se
/// sigue rechazando. El parseo devolvía <c>null</c> para las dos cosas, así que un arreglo que
/// tratara «null» como «no consta» habría dejado pasar un <c>assertion: "SuperFuerte"</c> como
/// no-consta — que es exactamente la mentira que el campo existe para impedir (defecto #42). Sin
/// este caso, el arreglo malo pasa en verde.</para>
/// </remarks>
public sealed class CobroContraLaCapacidadRealTests
{
    private static ArnesDeCapacidades ConPagos()
        => new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Payments.Contracts.MoneyDto>("Payments");

    [Fact]
    public async Task Un_cobro_SIN_afirmacion_lo_acepta_y_queda_como_no_consta()
    {
        using var arnes = ConPagos();
        using var http = arnes.CreateClient("Payments");
        http.DefaultRequestHeaders.Add("Idempotency-Key", "compra-1");

        var respuesta = await http.PostAsJsonAsync("v1/payments", new
        {
            forKind = "tienda.compra",
            forId = "saga-1",
            payerKind = "tienda.comprador",
            payerId = "seudonimo-abc",
            amount = new { amount = 100_000m, currency = "COP" },
        });

        Assert.Equal(System.Net.HttpStatusCode.Created, respuesta.StatusCode);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        // «no consta» viaja como nulo, que es lo que `PaymentResponse.PaidWith` ya documentaba:
        // «no consta» y «CmsSession» no son lo mismo, y afirmar el segundo sería inventar una
        // comprobación que nadie hizo.
        Assert.True(
            !cuerpo.TryGetProperty("paidWith", out var conQue) || conQue.ValueKind == JsonValueKind.Null,
            $"Se esperaba `paidWith` nulo («no consta») y salió: {cuerpo}");
    }

    [Fact]
    public async Task Una_afirmacion_ILEGIBLE_se_sigue_rechazando()
    {
        using var arnes = ConPagos();
        using var http = arnes.CreateClient("Payments");
        http.DefaultRequestHeaders.Add("Idempotency-Key", "compra-2");

        var respuesta = await http.PostAsJsonAsync("v1/payments", new
        {
            forKind = "tienda.compra",
            forId = "saga-2",
            payerKind = "tienda.comprador",
            payerId = "seudonimo-abc",
            amount = new { amount = 100_000m, currency = "COP" },
            assertion = "SuperFuerte",
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("access_requires_identity", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declarar_IdentityToken_sin_presentarlo_se_sigue_rechazando()
    {
        using var arnes = ConPagos();
        using var http = arnes.CreateClient("Payments");
        http.DefaultRequestHeaders.Add("Idempotency-Key", "compra-3");

        var respuesta = await http.PostAsJsonAsync("v1/payments", new
        {
            forKind = "tienda.compra",
            forId = "saga-3",
            payerKind = "tienda.comprador",
            payerId = "seudonimo-abc",
            amount = new { amount = 100_000m, currency = "COP" },
            assertion = nameof(IdentityAssertion.IdentityToken),
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains(
            "assertion_not_proven",
            await respuesta.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Una_capacidad_que_nadie_levanto_RECHAZA_en_vez_de_dar_404()
    {
        using var arnes = ConPagos();

        // Sin esto, un test que pidiera una capacidad que olvidó levantar correría contra un 404 y
        // su verde no diría nada — el verde sobre el vacío que este repo ya pagó cuatro veces.
        var falla = Assert.Throws<InvalidOperationException>(() => arnes.CreateClient("Inventory"));
        Assert.Contains("Inventory", falla.Message, StringComparison.Ordinal);
        Assert.Contains("Payments", falla.Message, StringComparison.Ordinal);
    }
}
