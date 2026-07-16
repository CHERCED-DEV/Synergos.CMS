using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests de <see cref="PaymentWebhookController"/> (T3, doc 25) — el receptor de
/// webhooks de pago ENTRANTES. Cubre: provider desconocido (404), payload inválido
/// (400), firma inválida (401), happy firmado (→ confirma la orden), idempotencia
/// (duplicado → 200 sin re-confirmar) y anti-tampering (sesión no autorizada → no
/// confirma).
/// </summary>
public sealed class PaymentWebhookControllerTests
{
    private readonly IPaymentProvider _payments = Substitute.For<IPaymentProvider>();
    private readonly IPaymentEventStore _events = Substitute.For<IPaymentEventStore>();
    private readonly IShopOrderService _orders = Substitute.For<IShopOrderService>();

    private static byte[] Payload(string eventId = "ev1", string sessionId = "stub_s", string orderRef = "ord_1")
        => Encoding.UTF8.GetBytes($"{{\"eventId\":\"{eventId}\",\"sessionId\":\"{sessionId}\",\"orderRef\":\"{orderRef}\",\"status\":\"approved\"}}");

    private PaymentWebhookController BuildSut(PaymentsSettings settings, byte[] body, string? ts = null, string? sig = null)
    {
        var ctrl = new PaymentWebhookController(
            _payments, _events, _orders, Options.Create(settings), NullLogger<PaymentWebhookController>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(body);
        if (ts is not null) http.Request.Headers[WebhookSigner.TimestampHeaderName] = ts;
        if (sig is not null) http.Request.Headers[WebhookSigner.SignatureHeaderName] = sig;
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static ShopOrder Order() => new(
        "ord_1", "SYN-1", OrderStatus.Pending, "Ana", "ana@x.co",
        Array.Empty<ShopOrderLine>(), 1000m, "COP", "stub_s", DateTimeOffset.UtcNow);

    private void ArrangeGoodSessionAndOrder()
    {
        _payments.GetStatusAsync("stub_s", Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome("stub_s", PaymentStatus.Authorized, 1000m));
        _orders.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Order());
        var confirmation = new ShopConfirmationResult("ord_1", "SYN-1", "Paid", Array.Empty<ShopOrderLine>(), 1000m, "COP");
        _orders.ConfirmAsync("ord_1", Arg.Any<CancellationToken>()).Returns(confirmation);
    }

    [Fact] // provider desconocido → 404
    public async Task UnknownProvider_Returns404()
    {
        var result = await BuildSut(new PaymentsSettings(), Payload()).Receive("paypal", default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact] // payload sin eventId/sessionId → 400
    public async Task BadPayload_Returns400()
    {
        var body = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
        var result = await BuildSut(new PaymentsSettings(), body).Receive("stub", default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact] // secret configurado + firma inválida → 401
    public async Task InvalidSignature_Returns401()
    {
        var settings = new PaymentsSettings { WebhookSecret = "shh" };
        var result = await BuildSut(settings, Payload(), ts: DateTimeOffset.UtcNow.ToString("O"), sig: "sha256=deadbeef")
            .Receive("stub", default);
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact] // happy (sin secret): sesión autorizada + orden existe → confirma → 200
    public async Task Happy_UnsignedDemo_ConfirmsOrder()
    {
        ArrangeGoodSessionAndOrder();
        _events.TryMarkProcessedAsync("stub", "ev1", Arg.Any<CancellationToken>()).Returns(true);

        var result = await BuildSut(new PaymentsSettings(), Payload()).Receive("stub", default);

        Assert.IsType<OkObjectResult>(result);
        await _orders.Received(1).ConfirmAsync("ord_1", Arg.Any<CancellationToken>());
    }

    [Fact] // happy FIRMADO: firmar con WebhookSigner + verificar aquí → confirma
    public async Task Happy_Signed_ConfirmsOrder()
    {
        ArrangeGoodSessionAndOrder();
        _events.TryMarkProcessedAsync("stub", "ev1", Arg.Any<CancellationToken>()).Returns(true);
        var body = Payload();
        var signed = WebhookSigner.ComputeSignedHeaders("shh", body)!.Value;

        var result = await BuildSut(new PaymentsSettings { WebhookSecret = "shh" }, body, signed.Timestamp, signed.Signature)
            .Receive("stub", default);

        Assert.IsType<OkObjectResult>(result);
        await _orders.Received(1).ConfirmAsync("ord_1", Arg.Any<CancellationToken>());
    }

    [Fact] // idempotente: evento duplicado (ledger dice ya procesado) → 200; ConfirmAsync
           // se re-ejecuta idempotente (mark-after: marcar antes dejaría la orden
           // cobrada-sin-confirmar si Confirm fallara transitoriamente).
    public async Task Duplicate_Returns200_ConfirmIsIdempotent()
    {
        ArrangeGoodSessionAndOrder();
        _events.TryMarkProcessedAsync("stub", "ev1", Arg.Any<CancellationToken>()).Returns(false);   // ya procesado

        var result = await BuildSut(new PaymentsSettings(), Payload()).Receive("stub", default);

        Assert.IsType<OkObjectResult>(result);
        await _orders.Received(1).ConfirmAsync("ord_1", Arg.Any<CancellationToken>());   // idempotente
        // NO se re-marca tras un duplicado (TryMark ya devolvió false, no se llama de nuevo).
    }

    [Fact] // anti-tampering: sesión de la orden NO autorizada (declinada) → no confirma, 200
    public async Task UnauthorizedSession_DoesNotConfirm()
    {
        _orders.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Order());   // PaymentSessionId=stub_s
        _payments.GetStatusAsync("stub_s", Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome("stub_s", PaymentStatus.Failed, 0m));

        var result = await BuildSut(new PaymentsSettings(), Payload()).Receive("stub", default);

        Assert.IsType<OkObjectResult>(result);
        await _orders.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // ligadura: la sesión del payload no es la de la orden → 200 ignorado, no confirma
    public async Task SessionMismatch_DoesNotConfirm()
    {
        _orders.GetOrderAsync("ord_1", Arg.Any<CancellationToken>()).Returns(Order());   // PaymentSessionId=stub_s
        // payload trae otra sessionId (stub_OTHER) que NO corresponde a la orden.
        var body = Payload(sessionId: "stub_OTHER");

        var result = await BuildSut(new PaymentsSettings(), body).Receive("stub", default);

        Assert.IsType<OkObjectResult>(result);
        await _orders.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payments.DidNotReceive().GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
