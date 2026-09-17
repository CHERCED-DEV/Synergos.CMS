using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Domain;
using Synergos.Api.Payments.Transport;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El borde hacia Wompi: qué se firma, qué se manda y cómo se clasifica lo que vuelve (HU #27).
/// </summary>
/// <remarks>
/// <para><b>Lo que más importa acá es en cuál de los cuatro cae cada respuesta</b>, y no se ve
/// mirando el código. <c>Declined</c> no se reintenta y <c>Unavailable</c> sí: un rechazo firme
/// clasificado como caída es una tormenta de peticiones contra una tarjeta que ya dijo que no, y
/// una caída clasificada como rechazo es una compra que se pierde por un hipo de red. Ninguna de
/// las dos se nota en un log — se notan en la factura o en la queja del comprador.</para>
///
/// <para><b>Y el 401 es el caso que parece cosmético y no lo es.</b> Mapearlo a <c>Declined</c>
/// le diría a quien compra que su banco dijo que no, cuando lo que pasa es que nuestra llave está
/// mal; y además lo dejaría sin reintento, así que el cobro se perdería aunque el operador
/// arreglara la credencial un minuto después.</para>
/// </remarks>
public sealed class PaymentTransportTests
{
    /// <summary>Una pasarela de mentira que contesta lo que le digan, o revienta.</summary>
    private sealed class Pasarela : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Vistas { get; } = new();
        public List<string?> Cuerpos { get; } = new();

        public Pasarela(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Vistas.Add(request);
            Cuerpos.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private const string Publica = "pub_test_abcdefghijklmnop";
    private const string Integridad = "test_integrity_ZYX987";
    private const string Privada = "prv_test_0123456789";

    private static WompiOptions Opciones() => new()
    {
        ApiKey = Privada,
        PublicKey = Publica,
        IntegritySecret = Integridad,
        EventsSecret = "test_events_QWE456",
        BaseUrl = "https://sandbox.ejemplo.co/v1/",
        CheckoutBaseUrl = "https://checkout.ejemplo.co/p/",
    };

    private static HttpResponseMessage Responde(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Lista(string estado, long centavos = 11900000, string id = "tx-1")
        => $$"""
            {"data":[{"id":"{{id}}","status":"{{estado}}","status_message":"dice el banco",
                      "amount_in_cents":{{centavos}},"reference":"syn-1"}]}
            """;

    private static (WompiPaymentProvider Proveedor, Pasarela Espia) Nuevo(
        Func<HttpRequestMessage, HttpResponseMessage> responder, WompiOptions? opciones = null)
    {
        var espia = new Pasarela(responder);
        var op = opciones ?? Opciones();
        var http = new HttpClient(espia) { BaseAddress = new Uri(op.BaseUrl) };
        return (new WompiPaymentProvider(http, Options.Create(op), NullLogger<WompiPaymentProvider>.Instance), espia);
    }

    private static readonly Ref Pagador = Ref.Create("identity.member", "m-1");
    private static Money Cop(decimal x) => Money.Of(x, "COP");

    private static HttpResponseMessage NoDeberia(HttpRequestMessage _)
        => throw new InvalidOperationException("no debería tocarse la red");

    // ── Autorizar: se firma, y no se toca la red ────────────────────────────

    [Fact]
    public async Task Autorizar_firma_el_checkout_SIN_salir_a_la_red()
    {
        // Con checkout hospedado la transacción nace cuando el comprador la completa: no hay a
        // quién preguntarle nada todavía. Si acá apareciera una petición, sería una llamada que
        // no hace falta en el camino más caliente del sistema.
        var (p, espia) = Nuevo(NoDeberia);

        var r = await p.AuthorizeAsync(Cop(119000), Pagador);

        Assert.True(r.IsOk);
        Assert.Empty(espia.Vistas);
        Assert.NotNull(r.Reference);
        Assert.NotNull(r.ActionUrl);
    }

    [Fact]
    public async Task La_URL_del_checkout_lleva_la_firma_que_Wompi_va_a_recalcular()
    {
        // La firma cubre referencia + centavos + moneda. Se comprueba contra el cálculo y no
        // contra una cadena quemada: quemarla probaría que el test sabe copiar, no que el
        // adaptador firma lo que manda.
        var (p, _) = Nuevo(NoDeberia);

        var r = await p.AuthorizeAsync(Cop(119000), Pagador);

        var esperada = WompiSignature.Integrity(r.Reference!, 11900000, "COP", Integridad);
        Assert.Contains($"signature%3Aintegrity={esperada}", r.ActionUrl, StringComparison.Ordinal);
        Assert.Contains("amount-in-cents=11900000", r.ActionUrl, StringComparison.Ordinal);
        Assert.Contains($"public-key={Publica}", r.ActionUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_referencia_NO_lleva_nada_del_pagador()
    {
        // Viaja en la URL del navegador del comprador y en los correos de la pasarela. Meter ahí
        // el identificador de un miembro sería publicarlo por un canal que no controlamos.
        var (p, _) = Nuevo(NoDeberia);

        var r = await p.AuthorizeAsync(Cop(50000), Ref.Create("identity.member", "ana@ejemplo.co"));

        Assert.DoesNotContain("ana", r.Reference, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ana", r.ActionUrl!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Una_moneda_que_Wompi_no_cobra_es_un_rechazo_FIRME_y_con_motivo()
    {
        // Es el único «no» que este adaptador puede dar sin red, y por eso es el que deja
        // ejercitar de verdad la rama «el pago falló, soltá el stock» de un orquestador: hasta
        // ahora esa rama solo la habían tocado dobles que decían que sí a todo.
        var (p, espia) = Nuevo(NoDeberia);

        var r = await p.AuthorizeAsync(Money.Of(120m, "USD"), Pagador);

        Assert.Equal(PaymentOutcome.Declined, r.Outcome);
        Assert.Contains("USD", r.Reason!, StringComparison.Ordinal);
        Assert.Empty(espia.Vistas);
    }

    [Fact]
    public async Task Sin_credencial_NO_se_toca_la_red_y_se_dice_QUE_falta()
    {
        // «No está configurado» manda a leer código; el nombre del ajuste manda a poner una
        // variable. Y no se intenta siquiera: llamar sin llave produciría un 401 que se leería
        // como problema del proveedor en vez de como el olvido que es.
        var sinSecreto = Opciones();
        sinSecreto.IntegritySecret = null;
        var (p, espia) = Nuevo(NoDeberia, sinSecreto);

        var r = await p.AuthorizeAsync(Cop(119000), Pagador);

        Assert.Equal(PaymentOutcome.NotConfigured, r.Outcome);
        Assert.Contains("IntegritySecret", r.Reason!, StringComparison.Ordinal);
        Assert.Empty(espia.Vistas);
    }

    // ── Capturar: constatar ─────────────────────────────────────────────────

    [Fact]
    public async Task Una_transaccion_aprobada_por_el_monto_pedido_captura()
    {
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista("APPROVED")));

        var r = await p.CaptureAsync("syn-1", Cop(119000));

        Assert.Equal(PaymentOutcome.Ok, r.Outcome);
        Assert.Equal("tx-1", r.Reference);
    }

    [Fact]
    public async Task Una_transaccion_aprobada_por_OTRO_monto_NO_se_da_por_capturada()
    {
        // La firma de integridad cubre el monto TAL COMO SE ENVIÓ, así que un error de centavos
        // produce una transacción impecable por la cifra equivocada. Sin esta comparación, cobrar
        // cien veces menos pasa el checkout, pasa la firma, y se descubre cuadrando la caja.
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista("APPROVED", centavos: 119000)));

        var r = await p.CaptureAsync("syn-1", Cop(119000));

        Assert.Equal(PaymentOutcome.Declined, r.Outcome);
        Assert.Contains("119000", r.Reason!, StringComparison.Ordinal);
        Assert.Contains("11900000", r.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_transaccion_que_TODAVIA_no_existe_es_transitoria()
    {
        // El comprador no completó el checkout. Es lo que deja al barrido reintentar y, al
        // rendirse, liberar una intención que nadie pagó. Darla por fallida cerraría la compra de
        // alguien que está tecleando su clave del banco.
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, """{"data":[]}"""));

        var r = await p.CaptureAsync("syn-1", Cop(119000));

        Assert.Equal(PaymentOutcome.Unavailable, r.Outcome);
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("ALGO_QUE_NO_EXISTIA_AYER")]
    public async Task Lo_que_Wompi_no_ha_resuelto_es_transitorio(string estado)
    {
        // Un estado nuevo que Wompi agregue mañana no puede hacer que el sistema dé por perdida
        // una compra que sigue viva.
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista(estado)));

        Assert.Equal(PaymentOutcome.Unavailable, (await p.CaptureAsync("syn-1", Cop(119000))).Outcome);
    }

    [Theory]
    [InlineData("DECLINED")]
    [InlineData("ERROR")]
    public async Task Un_rechazo_del_banco_es_FIRME_y_viaja_con_su_motivo(string estado)
    {
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista(estado)));

        var r = await p.CaptureAsync("syn-1", Cop(119000));

        Assert.Equal(PaymentOutcome.Declined, r.Outcome);
        Assert.Equal("dice el banco", r.Reason);
    }

    // ── Cómo se clasifica lo que vuelve ─────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Un_fallo_de_la_pasarela_es_TRANSITORIO(HttpStatusCode code)
    {
        var (p, _) = Nuevo(_ => Responde(code, """{"error":"ups"}"""));

        Assert.Equal(PaymentOutcome.Unavailable, (await p.CaptureAsync("syn-1", Cop(1000))).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Una_credencial_rechazada_NO_es_un_rechazo_del_comprador(HttpStatusCode code)
    {
        // Es un defecto de despliegue. Llamarlo Declined le diría a quien compra que su banco
        // dijo que no, y además lo dejaría sin reintento: el cobro se perdería aunque el operador
        // pusiera la llave buena un minuto después.
        var (p, _) = Nuevo(_ => Responde(code, """{"error":"invalid key"}"""));

        var r = await p.CaptureAsync("syn-1", Cop(1000));

        Assert.Equal(PaymentOutcome.NotConfigured, r.Outcome);
        Assert.Contains("Payments:wompi:ApiKey", r.Reason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Una_peticion_que_Wompi_rechaza_no_se_reintenta(HttpStatusCode code)
    {
        var (p, _) = Nuevo(_ => Responde(code, """{"error":"amount_in_cents"}"""));

        var r = await p.CaptureAsync("syn-1", Cop(1000));

        Assert.Equal(PaymentOutcome.Declined, r.Outcome);
        Assert.Contains("amount_in_cents", r.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_corte_de_red_no_se_escapa_como_excepcion()
    {
        // El contrato de la costura dice que NO lanza. Si se escapara, el cobro quedaría sin
        // registrar y el rastro perdería justo el caso que importa.
        var (p, _) = Nuevo(_ => throw new HttpRequestException("connection refused"));

        Assert.Equal(PaymentOutcome.Unavailable, (await p.CaptureAsync("syn-1", Cop(1000))).Outcome);
    }

    [Fact]
    public async Task Un_200_ilegible_es_transitorio_y_no_un_rechazo()
    {
        // De un cuerpo que no se entiende no se concluye nada. Darlo por rechazo cerraría una
        // compra por un despliegue raro de la pasarela.
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, "no soy json"));

        Assert.Equal(PaymentOutcome.Unavailable, (await p.CaptureAsync("syn-1", Cop(1000))).Outcome);
    }

    // ── Devolver ────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_devolucion_va_con_el_ID_DE_WOMPI_y_el_monto_en_centavos()
    {
        // Wompi devuelve por el identificador de la transacción, no por nuestra referencia: por
        // eso hay que buscarla antes. Mandar la referencia daría 404 siempre.
        var (p, espia) = Nuevo(r => r.Method == HttpMethod.Post
            ? Responde(HttpStatusCode.Created, """{"data":{"id":"rf-1"}}""")
            : Responde(HttpStatusCode.OK, Lista("APPROVED")));

        var r = await p.RefundAsync("syn-1", Cop(50000));

        Assert.Equal(PaymentOutcome.Ok, r.Outcome);
        var enviado = espia.Cuerpos.Last()!;
        Assert.Contains("\"transaction_id\":\"tx-1\"", enviado, StringComparison.Ordinal);
        Assert.Contains("\"amount_in_cents\":5000000", enviado, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_se_devuelve_lo_que_Wompi_nunca_aprobo()
    {
        var (p, espia) = Nuevo(r => r.Method == HttpMethod.Post
            ? throw new InvalidOperationException("no debería pedirse la devolución")
            : Responde(HttpStatusCode.OK, Lista("PENDING")));

        var r = await p.RefundAsync("syn-1", Cop(50000));

        Assert.Equal(PaymentOutcome.Declined, r.Outcome);
        Assert.Single(espia.Vistas);
    }

    // ── Liberar: donde la compensación cambia de carácter ───────────────────

    [Theory]
    [InlineData("DECLINED")]
    [InlineData("ERROR")]
    [InlineData("VOIDED")]
    public async Task Liberar_lo_que_nadie_pago_sale_bien(string estado)
    {
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista(estado)));

        Assert.Equal(PaymentOutcome.Ok, (await p.VoidAsync("syn-1")).Outcome);
    }

    [Fact]
    public async Task Liberar_una_intencion_que_nunca_llego_a_transaccion_sale_bien()
    {
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, """{"data":[]}"""));

        Assert.Equal(PaymentOutcome.Ok, (await p.VoidAsync("syn-1")).Outcome);
    }

    [Fact]
    public async Task Liberar_un_cobro_YA_APROBADO_se_rechaza_y_dice_que_eso_es_devolver()
    {
        // Es la compensación cambiando de carácter: antes de que la plata se mueva, deshacer es
        // «liberar»; después, «devolver». Decir que sí acá es cómo un comprador se queda sin su
        // dinero mientras el sistema anota que lo soltó.
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista("APPROVED")));

        var r = await p.VoidAsync("syn-1");

        Assert.Equal(PaymentOutcome.Declined, r.Outcome);
        Assert.Contains("devuelve", r.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Liberar_algo_TODAVIA_EN_CURSO_es_transitorio_y_no_un_si()
    {
        // Con PSE el desenlace llega minutos después. Contestar que se liberó sería mentir sobre
        // un cobro que quizá está ocurriendo; el compensador reintenta y, cuando resuelva, esto
        // contesta la verdad —sí se pudo, o hace falta una persona—.
        var (p, _) = Nuevo(_ => Responde(HttpStatusCode.OK, Lista("PENDING")));

        Assert.Equal(PaymentOutcome.Unavailable, (await p.VoidAsync("syn-1")).Outcome);
    }

    // ── Lo que el proveedor declara de sí mismo ─────────────────────────────

    [Fact]
    public async Task Este_proveedor_DECLARA_que_mueve_plata()
    {
        // Es lo que permite que el gate lo distinga de los dos que fingen, sin leer el nombre
        // configurado — que es exactamente lo que falló la vez pasada en el CMS.
        var (p, _) = Nuevo(NoDeberia);

        Assert.True(p.MuevePlata);
        Assert.Equal("wompi", p.Name);
        await Task.CompletedTask;
    }
}
