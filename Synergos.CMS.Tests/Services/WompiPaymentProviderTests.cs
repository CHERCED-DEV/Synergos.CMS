using System.Net;
using System.Text;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="WompiPaymentProvider"/> — el adapter sobre Wompi Web Checkout.
/// </summary>
/// <remarks>
/// <b>No toca la red.</b> Un <see cref="HttpMessageHandler"/> de mentira sirve
/// las respuestas, que es lo único que se puede hacer sin llaves de sandbox.
/// Lo que estos tests garantizan es que el adapter arma bien la URL, mapea bien
/// los estados y degrada bien ante fallos — no que Wompi lo acepte.
/// </remarks>
public sealed class WompiPaymentProviderTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CannedHandler(HttpStatusCode code, string body = "{}")
        {
            _code = code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_code)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("sin red");
    }

    private static PaymentsSettings Keys() => new()
    {
        WompiPublicKey = "pub_test_abc",
        WompiIntegritySecret = "test_integrity_xyz",
        ReturnUrlBase = "https://tienda.test/checkout/retorno",
    };

    private static WompiPaymentProvider Make(
        HttpMessageHandler handler, PaymentsSettings? settings = null)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.wompi.co/v1/") },
            settings ?? Keys());

    private static PaymentSessionRequest Req(string? method = null, decimal amount = 249_000m)
        => new("ord_abc123", amount, "COP",
            new[] { new PaymentLineItem("SKU", "Producto", amount, 1) },
            CustomerEmail: "ana@correo.co",
            PreferredMethod: method);

    // ── Creación de sesión ───────────────────────────────────────────────

    [Fact]
    public async Task La_sesion_devuelve_un_REDIRECT_al_checkout_hospedado()
    {
        var session = await Make(new CannedHandler(HttpStatusCode.OK)).CreateSessionAsync(Req());

        Assert.Equal(PaymentStatus.RequiresAction, session.Status);
        Assert.Equal(PaymentActionKind.Redirect, session.Action!.Kind);
        Assert.StartsWith("https://checkout.wompi.co/p/", session.Action.RedirectUrl!.ToString());
        Assert.Equal("wompi", session.ProviderKey);
    }

    [Fact]
    public async Task El_sessionId_es_la_REFERENCIA_del_comercio()
    {
        // Todavía no existe id de Wompi: la transacción nace cuando el cliente
        // completa el checkout. La referencia es además la llave con la que se
        // reconcilia después, así que tiene que ser la que viaja firmada.
        var session = await Make(new CannedHandler(HttpStatusCode.OK)).CreateSessionAsync(Req());

        Assert.Equal("ord_abc123", session.SessionId);
    }

    [Fact]
    public async Task La_URL_lleva_el_monto_en_CENTAVOS_y_la_firma_de_integridad()
    {
        var session = await Make(new CannedHandler(HttpStatusCode.OK)).CreateSessionAsync(Req());
        var url = session.Action!.RedirectUrl!.ToString();

        Assert.Contains("amount-in-cents=24900000", url, StringComparison.Ordinal);
        Assert.Contains("currency=COP", url, StringComparison.Ordinal);

        // La firma tiene que ser exactamente la que WompiSignature calcula para
        // esos mismos datos: si el adapter firmara otra cosa, Wompi rechazaría
        // la transacción con un mensaje que no dice cuál fue el problema.
        var expected = WompiSignature.Integrity(
            "ord_abc123", 24_900_000, "COP", "test_integrity_xyz");
        Assert.Contains(expected, url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Los_dos_puntos_del_parametro_de_firma_van_ESCAPADOS()
    {
        // "signature:integrity" lleva dos puntos. Sin escapar, la URL queda
        // ambigua y la firma viaja mal — exactamente el error nº1 de
        // integración que reportó la investigación de PSPs.
        var session = await Make(new CannedHandler(HttpStatusCode.OK)).CreateSessionAsync(Req());
        var url = session.Action!.RedirectUrl!.ToString();

        Assert.Contains("signature%3Aintegrity=", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pse", "PSE")]
    [InlineData("nequi", "NEQUI")]
    [InlineData("bancolombia", "BANCOLOMBIA_TRANSFER")]
    [InlineData("card", "CARD")]
    public async Task El_riel_preferido_viaja_como_sugerencia(string method, string wompiName)
    {
        var session = await Make(new CannedHandler(HttpStatusCode.OK)).CreateSessionAsync(Req(method));

        Assert.Contains($"payment-methods={wompiName}", session.Action!.RedirectUrl!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_riel_DESCONOCIDO_no_filtra_nada()
    {
        var session = await Make(new CannedHandler(HttpStatusCode.OK))
            .CreateSessionAsync(Req("criptomonedas"));

        Assert.DoesNotContain("payment-methods=", session.Action!.RedirectUrl!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_llaves_configuradas_FALLA_al_crear_y_no_al_cobrar()
    {
        // Un adapter que "casi" funciona es peor que uno que se niega: el
        // checkout llegaría hasta el final y fallaría en el paso más caro de
        // rehacer para el comprador.
        var sut = Make(new CannedHandler(HttpStatusCode.OK), new PaymentsSettings());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSessionAsync(Req()));
    }

    // ── Mapeo de estados ─────────────────────────────────────────────────

    [Theory]
    [InlineData("APPROVED", PaymentStatus.Captured)]
    [InlineData("DECLINED", PaymentStatus.Failed)]
    [InlineData("ERROR", PaymentStatus.Failed)]
    [InlineData("VOIDED", PaymentStatus.Cancelled)]
    [InlineData("PENDING", PaymentStatus.Pending)]
    [InlineData("approved", PaymentStatus.Captured)]
    [InlineData("  APPROVED  ", PaymentStatus.Captured)]
    public void Los_estados_de_Wompi_mapean_a_los_canonicos(string wompi, PaymentStatus expected)
        => Assert.Equal(expected, WompiPaymentProvider.MapStatus(wompi));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UN_ESTADO_NUEVO_DE_2027")]
    public void Un_estado_DESCONOCIDO_cae_a_Pending_y_no_a_Failed(string? unknown)
    {
        // Un estado que Wompi agregue mañana no puede hacer que demos por
        // perdida una compra que sigue viva.
        Assert.Equal(PaymentStatus.Pending, WompiPaymentProvider.MapStatus(unknown));
    }

    // ── Consulta de estado ───────────────────────────────────────────────

    [Fact]
    public async Task Una_transaccion_aprobada_reporta_el_monto_capturado()
    {
        var json = """
        {"data":[{"id":"113-160","status":"APPROVED","amount_in_cents":24900000,"reference":"ord_abc123"}]}
        """;
        var outcome = await Make(new CannedHandler(HttpStatusCode.OK, json))
            .GetStatusAsync("ord_abc123");

        Assert.Equal(PaymentStatus.Captured, outcome.Status);
        Assert.Equal(249_000m, outcome.AmountCaptured);
    }

    [Fact]
    public async Task Una_referencia_SIN_transaccion_esta_Pending_y_no_fallida()
    {
        // El cliente todavía no completó el checkout. Marcarlo Failed liberaría
        // el stock de una compra que aún puede ocurrir.
        var outcome = await Make(new CannedHandler(HttpStatusCode.OK, """{"data":[]}"""))
            .GetStatusAsync("ord_abc123");

        Assert.Equal(PaymentStatus.Pending, outcome.Status);
    }

    [Fact]
    public async Task Un_5xx_de_Wompi_es_estado_DESCONOCIDO_no_pago_fallido()
    {
        // La distinción que importa: "no sé" no es "no". Devolver Failed haría
        // que el llamante compensara una compra que quizá sí ocurrió.
        var outcome = await Make(new CannedHandler(HttpStatusCode.InternalServerError))
            .GetStatusAsync("ord_abc123");

        Assert.Equal(PaymentStatus.Pending, outcome.Status);
        Assert.Contains("desconocido", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sin_red_tampoco_se_da_por_fallido()
    {
        var outcome = await Make(new ThrowingHandler()).GetStatusAsync("ord_abc123");

        Assert.Equal(PaymentStatus.Pending, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureReason));
    }

    // ── Void y Refund ────────────────────────────────────────────────────

    [Fact]
    public async Task Void_sobre_algo_NO_cobrado_cancela()
    {
        var outcome = await Make(new CannedHandler(HttpStatusCode.OK, """{"data":[]}"""))
            .VoidAsync("ord_abc123");

        Assert.Equal(PaymentStatus.Cancelled, outcome.Status);
        Assert.Equal(0m, outcome.AmountCaptured);
    }

    [Fact]
    public async Task Void_sobre_algo_YA_cobrado_no_miente_y_manda_a_Refund()
    {
        var json = """
        {"data":[{"id":"1","status":"APPROVED","amount_in_cents":100000,"reference":"ord_abc123"}]}
        """;
        var outcome = await Make(new CannedHandler(HttpStatusCode.OK, json)).VoidAsync("ord_abc123");

        Assert.Equal(PaymentStatus.Captured, outcome.Status);
        Assert.Contains("RefundAsync", outcome.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refund_sobre_algo_NO_capturado_se_niega()
    {
        var outcome = await Make(new CannedHandler(HttpStatusCode.OK, """{"data":[]}"""))
            .RefundAsync("ord_abc123");

        Assert.NotEqual(PaymentStatus.Refunded, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureReason));
    }

    [Fact]
    public async Task Un_reembolso_RECHAZADO_deja_el_estado_en_Captured()
    {
        // Marcarlo Refunded sin confirmación dejaría al comprador sin su plata y
        // al comercio creyendo que se la devolvió. Es de los errores que sólo se
        // descubren cuando alguien reclama.
        var handler = new CannedHandler(HttpStatusCode.UnprocessableEntity, """
        {"data":[{"id":"1","status":"APPROVED","amount_in_cents":100000,"reference":"ord_abc123"}]}
        """);
        var outcome = await Make(handler).RefundAsync("ord_abc123");

        Assert.NotEqual(PaymentStatus.Refunded, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureReason));
    }
}
