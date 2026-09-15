using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El cobro contra <c>Api.Payments</c> (#27, la parte que quedó viva).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba no es «cobrar». Es lo que este camino tiene de distinto respecto del
/// motor en proceso, que son tres cosas y ninguna se ve en el camino feliz:</para>
///
/// <list type="number">
///   <item><b>La llave de idempotencia sale de la referencia de orden</b> —que el llamador acuña
///   antes de pedir nada—, así que un reintento tras un timeout encuentra el cobro que él mismo
///   creó. Con el motor en proceso esto no hacía falta y por eso no estaba.</item>
///   <item><b>«No» y «no sé» salen distinto</b>: un rechazo firme es una respuesta y se devuelve
///   como <c>Failed</c>; una caída lanza, porque el seam no sabe decir «no sé» y contestar
///   <c>Failed</c> ahí afirmaría que el banco dijo que no.</item>
///   <item><b>El <c>actionUrl</c> se lee.</b> Con checkout hospedado la transacción nace cuando
///   el comprador la completa: leerlo es lo que impide que el CMS capture una intención que nadie
///   pagó.</item>
/// </list>
/// </remarks>
public sealed class HttpPaymentProviderTests
{
    private const string Autorizado = """
        {"id":"pay_1","status":"Authorized","amount":{"amount":95000,"currency":"COP"},
         "refunded":{"amount":0,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}
        """;

    private const string Capturado = """
        {"id":"pay_1","status":"Captured","amount":{"amount":95000,"currency":"COP"},
         "refunded":{"amount":0,"currency":"COP"},"refundable":{"amount":95000,"currency":"COP"}}
        """;

    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Method, string Path, string? Key, string? Body)> Llamadas { get; } = new();

        public CapacidadFalsa Ok(string ruta, string json, HttpStatusCode codigo = HttpStatusCode.OK)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public CapacidadFalsa Falla(string ruta, HttpStatusCode codigo, string code, string detail, bool transient)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""
                      {"title":"x","status":{{(int)codigo}},"detail":"{{detail}}",
                       "code":"{{code}}","transient":{{(transient ? "true" : "false")}}}
                      """,
                    Encoding.UTF8, "application/problem+json"),
            };
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            Llamadas.Add((req.Method.Method, path,
                req.Headers.TryGetValues("Idempotency-Key", out var k) ? k.FirstOrDefault() : null,
                req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult()));

            return Task.FromResult(_rutas.TryGetValue($"{req.Method.Method} {path}", out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FabricaFalsa(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://payments.local/") };
    }

    private sealed class CaidaTotal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => throw new HttpRequestException("Connection refused (127.0.0.1:5204)");
    }

    private static HttpPaymentProvider Provider(HttpMessageHandler handler)
        => new(new FabricaFalsa(handler), "x",
            () => new PaymentWireKinds("gov.expediente", "gov.ciudadano", "gov"),
            NullLogger<HttpPaymentProvider>.Instance);

    private static PaymentSessionRequest Peticion(string orden = "SG-2026-000042") => new(
        OrderReference: orden,
        Amount: 95_000m,
        Currency: "COP",
        Items: new[] { new PaymentLineItem("trm-pasaporte", "Tasa", 95_000m, 1) },
        CustomerEmail: "ana.torres@correo.co",
        Vertical: "gov");

    // ── happy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Autorizar_manda_la_llave_de_idempotencia_DEL_LLAMADOR()
    {
        // Es lo único que impide que un reintento tras un timeout cobre dos veces, y tiene que
        // salir de la referencia que trajo la petición: derivarla de la respuesta anterior —o de
        // un GUID nuevo— daría una llave distinta en cada intento, o sea ninguna llave.
        var capacidad = new CapacidadFalsa().Ok("POST /v1/payments", Autorizado, HttpStatusCode.Created);

        var sesion = await Provider(capacidad).CreateSessionAsync(Peticion());

        Assert.Equal("pay_1", sesion.SessionId);
        Assert.Equal(PaymentStatus.Authorized, sesion.Status);
        Assert.Equal("gov:sg-2026-000042", capacidad.Llamadas.Single().Key);
    }

    [Fact]
    public async Task Al_pagador_le_viaja_un_seudonimo_y_NO_su_correo()
    {
        // Api.Payments cuenta plata, no personas. Mandarle direcciones de correo las esparce sin
        // ninguna ganancia — y las copias de la HU #31 ya llevan bastantes datos personales.
        var capacidad = new CapacidadFalsa().Ok("POST /v1/payments", Autorizado, HttpStatusCode.Created);

        await Provider(capacidad).CreateSessionAsync(Peticion());

        var cuerpo = capacidad.Llamadas.Single().Body!;
        Assert.DoesNotContain("ana.torres@correo.co", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gov.expediente", cuerpo, StringComparison.Ordinal);
        Assert.Contains("SG-2026-000042", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capturar_devuelve_lo_capturado()
    {
        var capacidad = new CapacidadFalsa().Ok("POST /v1/payments/pay_1/capture", Capturado);

        var salida = await Provider(capacidad).CaptureAsync("pay_1");

        Assert.Equal(PaymentStatus.Captured, salida.Status);
        Assert.Equal(95_000m, salida.AmountCaptured);
        Assert.Equal("gov:pay_1:capture", capacidad.Llamadas.Single().Key);
    }

    // ── el actionUrl, que es lo que nadie leía ──────────────────────────────

    [Fact]
    public async Task Con_checkout_hospedado_la_sesion_pide_ir_a_pagar_y_NO_dice_autorizada()
    {
        // La capacidad emitía `actionUrl` desde la HU #27 sin un solo consumidor. Leerlo no es
        // cosmética: con checkout hospedado la transacción NACE cuando el comprador la completa,
        // así que una sesión con acción pendiente no tiene nada que capturar. Decir «Authorized»
        // haría que el llamador capturara una intención que nadie pagó — y escribiera un fallo
        // que nadie causó.
        var capacidad = new CapacidadFalsa().Ok("POST /v1/payments", """
            {"id":"pay_9","status":"Authorized","amount":{"amount":95000,"currency":"COP"},
             "refunded":{"amount":0,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"},
             "actionUrl":"https://checkout.wompi.co/l/abc123"}
            """, HttpStatusCode.Created);

        var sesion = await Provider(capacidad).CreateSessionAsync(Peticion());

        Assert.Equal(PaymentStatus.RequiresAction, sesion.Status);
        Assert.Equal(PaymentActionKind.Redirect, sesion.Action!.Kind);
        Assert.Equal("https://checkout.wompi.co/l/abc123", sesion.Action.RedirectUrl!.ToString());
    }

    // ── «no» y «no sé» ──────────────────────────────────────────────────────

    [Fact]
    public async Task Un_rechazo_FIRME_es_una_respuesta_y_no_lanza()
    {
        // «Fondos insuficientes» no se reintenta: la respuesta no va a cambiar. El llamador tiene
        // que poder seguir adelante con su propia decisión.
        var capacidad = new CapacidadFalsa().Falla("POST /v1/payments", HttpStatusCode.Conflict,
            "payments.payment_declined", "Fondos insuficientes", transient: false);

        var sesion = await Provider(capacidad).CreateSessionAsync(Peticion());

        Assert.Equal(PaymentStatus.Failed, sesion.Status);
    }

    [Fact]
    public async Task Una_capacidad_que_no_contesta_NO_se_lee_como_un_rechazo()
    {
        // Es la distinción que la capacidad ya hace por nosotros y que este lado tiene que
        // respetar: contestar Failed aquí afirmaría que el banco dijo que no, y eso lleva a no
        // reintentar nunca algo que sólo estaba caído.
        await Assert.ThrowsAnyAsync<Exception>(
            () => Provider(new CaidaTotal()).CreateSessionAsync(Peticion()));

        var transitorio = new CapacidadFalsa().Falla("POST /v1/payments", HttpStatusCode.ServiceUnavailable,
            "payments.payment_provider_unavailable", "la pasarela no contestó", transient: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(transitorio).CreateSessionAsync(Peticion()));
    }

    [Fact]
    public async Task Un_cuerpo_ilegible_es_no_se_y_NO_un_rechazo()
    {
        // Un intermediario que devuelve HTML no es el banco diciendo que no. Tratarlo como
        // rechazo firme convertiría cualquier proxy mal puesto en «su tarjeta fue rechazada».
        var capacidad = new CapacidadFalsa();
        capacidad.Ok("POST /v1/payments", "<html>502</html>", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(capacidad).CreateSessionAsync(Peticion()));
    }

    // ── filter / empty ──────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_referencia_de_orden_NO_se_cobra()
    {
        // Sin ella no hay llave de idempotencia estable, y sin llave un reintento cobra dos
        // veces. Es preferible no cobrar a cobrar sin poder repetir la llamada.
        await Assert.ThrowsAsync<ArgumentException>(
            () => Provider(new CapacidadFalsa()).CreateSessionAsync(Peticion(orden: "  ")));
    }

    [Fact]
    public async Task Una_sesion_que_no_existe_se_dice_y_no_se_inventa()
    {
        var salida = await Provider(new CapacidadFalsa()).GetStatusAsync("pay_no");

        Assert.Equal(PaymentStatus.Failed, salida.Status);
        Assert.Equal("Sesión de pago no encontrada.", salida.FailureReason);
    }

    [Fact]
    public async Task La_captura_PARCIAL_se_rechaza_diciendolo_en_vez_de_cobrar_el_total()
    {
        // El seam la admite —cobrar por noche, cobrar sólo lo despachado— y Api.Payments captura
        // lo autorizado o nada. Capturar el total «porque es lo que hay» cobraría de más, y el
        // llamador lo leería como un éxito.
        var capacidad = new CapacidadFalsa()
            .Ok("GET /v1/payments/pay_1", Autorizado)
            .Ok("POST /v1/payments/pay_1/capture", Capturado);

        var salida = await Provider(capacidad).CaptureAsync("pay_1", amount: 40_000m);

        Assert.DoesNotContain(capacidad.Llamadas, l => l.Method == "POST");
        Assert.Contains("captura lo autorizado", salida.FailureReason!, StringComparison.Ordinal);
    }

    // ── devolver ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_monto_se_devuelve_lo_que_QUEDA_por_devolver()
    {
        // El endpoint exige un monto y no tiene la noción de «todo»: se lee de la capacidad, que
        // es la que lleva la cuenta. Inventarlo con el total autorizado devolvería de más cuando
        // ya hubo una devolución parcial.
        var capacidad = new CapacidadFalsa()
            .Ok("GET /v1/payments/pay_1", """
                {"id":"pay_1","status":"Captured","amount":{"amount":95000,"currency":"COP"},
                 "refunded":{"amount":30000,"currency":"COP"},"refundable":{"amount":65000,"currency":"COP"}}
                """)
            .Ok("POST /v1/payments/pay_1/refund", """
                {"id":"pay_1","status":"Captured","amount":{"amount":95000,"currency":"COP"},
                 "refunded":{"amount":95000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}
                """);

        var salida = await Provider(capacidad).RefundAsync("pay_1");

        var post = capacidad.Llamadas.Single(l => l.Method == "POST");
        Assert.Contains("65000", post.Body!, StringComparison.Ordinal);

        // Devolver es un movimiento RELATIVO: lleva llave o un reintento devuelve dos veces.
        Assert.Equal("gov:pay_1:refund:65000", post.Key);
        Assert.Equal(PaymentStatus.Refunded, salida.Status);
        Assert.Equal(95_000m, salida.AmountRefunded);
    }

    [Fact]
    public async Task Liberar_NO_lleva_llave()
    {
        // La operación ya es idempotente por diseño —liberar lo liberado devuelve lo mismo— y
        // exigir una cabecera que no protege de nada sólo enseña a los clientes a inventar
        // llaves. Es la misma decisión que tomó el endpoint.
        var capacidad = new CapacidadFalsa().Ok("POST /v1/payments/pay_1/void", """
            {"id":"pay_1","status":"Voided","amount":{"amount":95000,"currency":"COP"},
             "refunded":{"amount":0,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}
            """);

        var salida = await Provider(capacidad).VoidAsync("pay_1");

        Assert.Null(capacidad.Llamadas.Single().Key);
        // «Liberado sin cobrar» es Cancelled acá; Refunded diría que salió plata que nunca entró.
        Assert.Equal(PaymentStatus.Cancelled, salida.Status);
    }

    [Fact]
    public async Task Un_rechazo_POR_ESTADO_devuelve_lo_que_de_verdad_hay()
    {
        // «Ya se capturó» no es un fallo: es el estado. Devolver AmountCaptured en cero sobre un
        // cobro capturado sería un dato falso, y el llamador lo escribiría en su expediente.
        var capacidad = new CapacidadFalsa()
            .Falla("POST /v1/payments/pay_1/capture", HttpStatusCode.Conflict,
                "payments.already_captured", "Ese cobro ya se capturó.", transient: false)
            .Ok("GET /v1/payments/pay_1", Capturado);

        var salida = await Provider(capacidad).CaptureAsync("pay_1");

        Assert.Equal(PaymentStatus.Captured, salida.Status);
        Assert.Equal(95_000m, salida.AmountCaptured);
        Assert.Contains("ya se capturó", salida.FailureReason!, StringComparison.Ordinal);
    }
}
